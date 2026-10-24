extends SceneTree

# Real Godot GLTFDocument import and GPU rendering. No editor imports or app code
# are needed. Every thumbnail uses the catalog model scale and mesh world bounds.
const THUMB_ROOT := "res://assets/content/thumbnails/"
const WIDTH := 160
const HEIGHT := 128
var viewport: SubViewport
var stage: Node3D
var camera: Camera3D
var audit: Array = []
var failures: Array = []
var rendered_models := 0
var rendered_motions := 0
var limit := 10000

func _initialize() -> void:
	call_deferred("run")

func find_nodes(node: Node, type: String) -> Array:
	var result: Array = []
	if node.is_class(type):
		result.append(node)
	for child in node.get_children():
		result.append_array(find_nodes(child, type))
	return result

func read_array(path: String) -> Array:
	return JSON.parse_string(FileAccess.get_file_as_string(path))

func mesh_bounds(model: Node) -> Dictionary:
	var meshes: Array = find_nodes(model, "MeshInstance3D")
	var bounds := AABB()
	var first := true
	var surfaces := 0
	var materials := 0
	var missing_materials := 0
	for instance: MeshInstance3D in meshes:
		if instance.mesh == null:
			continue
		var transformed: AABB = instance.global_transform * instance.get_aabb()
		if first:
			bounds = transformed
			first = false
		else:
			bounds = bounds.merge(transformed)
		for surface in range(instance.mesh.get_surface_count()):
			surfaces += 1
			if instance.get_active_material(surface) != null:
				materials += 1
			else:
				missing_materials += 1
	return {"Bounds": bounds, "Meshes": meshes.size(), "Surfaces": surfaces,
		"MaterialSurfaces": materials, "MissingMaterialSurfaces": missing_materials}

func frame_bounds(bounds: AABB) -> void:
	var center := bounds.get_center()
	var distance := maxf(bounds.size.length(), 1)
	camera.position = center + Vector3(3.4, 2.6, 4.4).normalized() * distance * 2
	camera.look_at(center)
	var projected_min := Vector2(INF, INF)
	var projected_max := Vector2(-INF, -INF)
	for i in range(8):
		var point := bounds.get_endpoint(i) - center
		var projected := Vector2(point.dot(camera.global_basis.x), point.dot(camera.global_basis.y))
		projected_min = projected_min.min(projected)
		projected_max = projected_max.max(projected)
	var extent := projected_max - projected_min
	camera.size = maxf(maxf(extent.y, extent.x / (float(WIDTH) / HEIGHT)) * 1.22, .01)
	camera.near = maxf(distance * .0001, .0001)
	camera.far = distance * 10

func capture(id: String) -> bool:
	# Allow skeleton updates, render submission, then await its actual GPU result.
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	var pixels := viewport.get_texture().get_image()
	if pixels == null or pixels.is_empty():
		failures.append({"Id": id, "Error": "Empty viewport image"})
		return false
	var error := pixels.save_png(THUMB_ROOT + id + ".png")
	if error != OK:
		failures.append({"Id": id, "Error": "PNG write failed: " + str(error)})
		return false
	return true

func render_model(entry: Dictionary, motions: Array) -> void:
	var source: String = entry.SourcePath
	var raw_path := source + ".bytes" if FileAccess.file_exists(source + ".bytes") else source
	if not FileAccess.file_exists(raw_path):
		failures.append({"Id": entry.Id, "Error": "Missing source: " + raw_path})
		return
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	var error := document.append_from_buffer(FileAccess.get_file_as_bytes(raw_path), source.get_base_dir(), state)
	if error != OK:
		failures.append({"Id": entry.Id, "Error": "GLTF import: " + str(error)})
		return
	var model := document.generate_scene(state)
	if model == null:
		failures.append({"Id": entry.Id, "Error": "GLTF generated no scene"})
		return
	var wrapper := Node3D.new()
	wrapper.scale = Vector3.ONE * float(entry.Scale)
	stage.add_child(wrapper)
	wrapper.add_child(model)
	var players := find_nodes(model, "AnimationPlayer")
	var player: AnimationPlayer = players[0] if not players.is_empty() else null
	if player != null and entry.get("DefaultAnimation") != null and player.has_animation(entry.DefaultAnimation):
		player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
		player.play(entry.DefaultAnimation)
		player.seek(.33 * player.get_animation(entry.DefaultAnimation).length, true)
		player.advance(0)
	await process_frame
	var metrics := mesh_bounds(model)
	var bounds: AABB = metrics.Bounds
	if metrics.Meshes == 0 or metrics.Surfaces == 0 or bounds.size.length() < .00001:
		failures.append({"Id": entry.Id, "Error": "No usable mesh bounds", "Meshes": metrics.Meshes, "Surfaces": metrics.Surfaces})
		wrapper.free()
		return
	frame_bounds(bounds)
	if await capture(entry.Id):
		rendered_models += 1
	var bones := 0
	for skeleton in find_nodes(model, "Skeleton3D"):
		bones += skeleton.get_bone_count()
	audit.append({"Id": entry.Id, "Name": entry.Name, "SourcePath": source, "Scale": entry.Scale,
		"Meshes": metrics.Meshes, "Surfaces": metrics.Surfaces, "MaterialSurfaces": metrics.MaterialSurfaces,
		"MissingMaterialSurfaces": metrics.MissingMaterialSurfaces, "BoundsMin": [bounds.position.x, bounds.position.y, bounds.position.z],
		"BoundsSize": [bounds.size.x, bounds.size.y, bounds.size.z], "Bones": bones,
		"AnimationCount": player.get_animation_list().size() if player != null else 0})
	if player != null:
		for motion in motions:
			if motion.ActorFamily != entry.ActorFamily:
				continue
			if not player.has_animation(motion.SourceAnimation):
				failures.append({"Id": motion.Id, "Error": "Missing animation " + str(motion.SourceAnimation)})
				continue
			player.play(motion.SourceAnimation)
			player.seek(float(motion.Duration) * .33, true)
			player.advance(0)
			if await capture(motion.Id):
				rendered_motions += 1
	wrapper.free()
	if rendered_models % 25 == 0 or entry.Category == "Animals":
		print("CONTENT_AUDIT models=", rendered_models, " animal motions=", rendered_motions, " latest=", entry.Id)

func run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--limit="):
			limit = argument.trim_prefix("--limit=").to_int()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(THUMB_ROOT))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://output"))
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	viewport = SubViewport.new()
	viewport.size = Vector2i(WIDTH, HEIGHT)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	viewport.msaa_3d = Viewport.MSAA_4X
	root.add_child(viewport)
	stage = Node3D.new()
	viewport.add_child(stage)
	var world := WorldEnvironment.new()
	world.environment = Environment.new()
	world.environment.background_mode = Environment.BG_COLOR
	world.environment.background_color = Color("252529")
	world.environment.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	world.environment.ambient_light_sky_contribution = 1
	# Some creator packs intentionally mark even wood/leaf colours as metallic.
	# A neutral reflection environment keeps those real source materials visible
	# without adding a directional light or altering the downloaded assets.
	var sky := Sky.new()
	var sky_material := ProceduralSkyMaterial.new()
	sky_material.sky_top_color = Color("c4c4c4")
	sky_material.sky_horizon_color = Color("969696")
	sky_material.ground_bottom_color = Color("505050")
	sky_material.ground_horizon_color = Color("969696")
	sky_material.sky_curve = .45
	sky_material.ground_curve = .45
	sky_material.sky_energy_multiplier = 1
	sky_material.ground_energy_multiplier = 1
	sky_material.energy_multiplier = 1
	sky.sky_material = sky_material
	world.environment.sky = sky
	world.environment.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	world.environment.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	stage.add_child(world)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.keep_aspect = Camera3D.KEEP_HEIGHT
	stage.add_child(camera)
	camera.current = true
	var models := read_array("res://assets/content/manifests/prop-models.json")
	models.append_array(read_array("res://assets/content/manifests/animal-models.json"))
	var motions := read_array("res://assets/content/manifests/animal-motions.json")
	for i in range(mini(models.size(), limit)):
		await render_model(models[i], motions)
	var report := {"Engine": Engine.get_version_info().string, "RenderingMethod": RenderingServer.get_current_rendering_method(),
		"Lighting": "Neutral ambient only; no directional light", "ThumbnailSize": [WIDTH, HEIGHT],
		"RenderedModels": rendered_models, "RenderedAnimalMotions": rendered_motions, "Failures": failures, "Models": audit}
	var file := FileAccess.open("res://output/content-audit.json", FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "  ") + "\n")
	print("CONTENT_AUDIT_COMPLETE models=", rendered_models, " animal motions=", rendered_motions, " failures=", failures.size())
	quit(0 if failures.is_empty() else 1)
