extends SceneTree
const DIR = "res://output/library-expansion-2026-10-26/"
func descendants(n: Node) -> Array:
	var found: Array = [n]
	for child in n.get_children(): found.append_array(descendants(child))
	return found
func _initialize(): call_deferred("run")
func run():
	var textures = []
	for name in DirAccess.get_files_at(DIR+"rabbit-extracted/texture/"):
		if not name.ends_with(".png"): continue
		var path = ProjectSettings.globalize_path(DIR+"rabbit-extracted/texture/"+name)
		var image = Image.load_from_file(path)
		var texture = ImageTexture.create_from_image(image)
		texture.take_over_path(path.replace("\\","/"))
		textures.append(texture)
	var sources = JSON.parse_string(FileAccess.get_file_as_string(DIR+"animal-source-inputs.json"))
	var results = []
	for entry in sources:
		var doc = FBXDocument.new()
		var state = FBXState.new()
		var error = doc.append_from_file(entry.path.replace("\\","/"),state)
		if error!=OK:
			print("FBX_REJECTED "+entry.id+" "+str(error))
			continue
		var model = doc.generate_scene(state)
		root.add_child(model)
		for mesh in descendants(model).filter(func(n):return n is MeshInstance3D):
			for surface in mesh.mesh.get_surface_count():
				var material = mesh.get_active_material(surface)
				if material is StandardMaterial3D and entry.id=="rabbit-cdmir":
					material.albedo_texture = textures[2] if textures.size()>2 else textures[0]
		var skeletons = descendants(model).filter(func(n):return n is Skeleton3D)
		var players = descendants(model).filter(func(n):return n is AnimationPlayer)
		var bones = []
		for skeleton in skeletons:
			for index in skeleton.get_bone_count():
				var position = skeleton.global_transform*skeleton.get_bone_global_pose(index).origin
				bones.append({"name":skeleton.get_bone_name(index),"parent":skeleton.get_bone_parent(index),"position":[position.x,position.y,position.z]})
		var animations = []
		for player in players:
			player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
			for name in player.get_animation_list():
				var animation = player.get_animation(name)
				animations.append({"name":name,"duration":animation.length,"tracks":animation.get_track_count()})
		var converted = GLTFDocument.new()
		var output = GLTFState.new()
		var target = DIR+entry.id+".glb"
		var exported = converted.append_from_scene(model,output)
		if exported==OK: exported=converted.write_to_filesystem(output,target)
		results.append({"id":entry.id,"name":entry.name,"source":entry.path,"license":entry.license,"author":entry.publisher,"bones":bones,"animations":animations,"glb":ProjectSettings.globalize_path(target),"export_error":exported})
		print("FBX_PROBE %s bones=%d animations=%d export=%d"%[entry.id,bones.size(),animations.size(),exported])
		model.queue_free()
		await process_frame
	FileAccess.open(DIR+"animal-fbx-audit.json",FileAccess.WRITE).store_string(JSON.stringify(results,"  "))
	print("FBX_PROBE_OK models="+str(results.size()))
	quit(0)
