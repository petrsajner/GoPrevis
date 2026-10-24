extends SceneTree

var failed := false
var total_clips := 0
var report: Array = []

func descendants(node: Node) -> Array:
	var result: Array = [node]
	for child in node.get_children():
		result.append_array(descendants(child))
	return result

func poses(skeletons: Array) -> Array:
	var result: Array = []
	for skeleton in skeletons:
		for bone in range(skeleton.get_bone_count()):
			result.append(skeleton.get_bone_pose(bone))
	return result

func pose_difference(a: Array, b: Array) -> float:
	var difference := 0.0
	for i in range(a.size()):
		var first: Transform3D = a[i]
		var second: Transform3D = b[i]
		difference = maxf(difference, first.origin.distance_to(second.origin))
		difference = maxf(difference, first.basis.get_rotation_quaternion().angle_to(second.basis.get_rotation_quaternion()))
	return difference

func check_model(model: Dictionary, motions: Array) -> void:
	var data := FileAccess.get_file_as_bytes(model.SourcePath + ".bytes")
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	var error := document.append_from_buffer(data, "", state)
	if error != OK:
		push_error("Animal import failed: " + model.Id + " " + str(error))
		failed = true
		return
	var scene := document.generate_scene(state)
	root.add_child(scene)
	var skeletons: Array = []
	var player: AnimationPlayer
	for node in descendants(scene):
		if node is Skeleton3D:
			skeletons.append(node)
		if node is AnimationPlayer:
			player = node
	if player == null or skeletons.is_empty():
		push_error("Missing animal animation rig: " + model.Id)
		failed = true
		scene.free()
		return
	player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	var bone_count := 0
	for skeleton in skeletons:
		bone_count += skeleton.get_bone_count()
	var clips: Array = []
	for motion in motions:
		if motion.ActorFamily != model.ActorFamily:
			continue
		var name: String = motion.SourceAnimation
		if not player.has_animation(name):
			push_error("Missing imported clip: " + model.Id + " / " + name + " available=" + str(player.get_animation_list()))
			failed = true
			continue
		var animation := player.get_animation(name)
		animation.loop_mode = Animation.LOOP_NONE
		player.play(name)
		player.seek(0, true)
		player.advance(0)
		var start := poses(skeletons)
		var change := 0.0
		for fraction in [0.21, 0.43, 0.77]:
			player.seek(animation.length * fraction, true)
			player.advance(0)
			change = maxf(change, pose_difference(start, poses(skeletons)))
		if change <= 0.00001 or absf(animation.length - motion.Duration) > 0.02:
			push_error("Invalid animal clip: " + model.Id + " / " + name + " change=" + str(change) + " duration=" + str(animation.length))
			failed = true
		clips.append({"Name": name, "Duration": animation.length, "Tracks": animation.get_track_count(), "PoseChange": change})
		total_clips += 1
	report.append({"Id": model.Id, "Bones": bone_count, "Animations": clips})
	print("ANIMAL_OK ", model.Name, " bones=", bone_count, " clips=", clips.size())
	scene.free()

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var models: Array = JSON.parse_string(FileAccess.get_file_as_string("res://assets/content/manifests/animal-models.json"))
	var motions: Array = JSON.parse_string(FileAccess.get_file_as_string("res://assets/content/manifests/animal-motions.json"))
	for model in models:
		check_model(model, motions)
	var file := FileAccess.open("res://assets/content/animals/validation.json", FileAccess.WRITE)
	file.store_string(JSON.stringify({"Engine": Engine.get_version_info().string, "Passed": not failed, "Models": report, "TotalClips": total_clips}, "  ") + "\n")
	print("ANIMAL_VALIDATION models=", models.size(), " clips=", total_clips, " passed=", not failed)
	quit(1 if failed else 0)
