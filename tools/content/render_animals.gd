extends SceneTree

var viewports: Array[SubViewport] = []

func _initialize() -> void:
	call_deferred("render_all")

func render_all() -> void:
	var models: Array = JSON.parse_string(FileAccess.get_file_as_string("res://assets/content/manifests/animal-models.json"))
	var provenance: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/content/animals/provenance.json"))
	for i in range(models.size()):
		var model: Dictionary = models[i]
		var metrics: Dictionary = provenance.Models[i]
		var viewport := SubViewport.new()
		viewport.size = Vector2i(320, 300)
		viewport.own_world_3d = true
		viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
		root.add_child(viewport)
		viewports.append(viewport)
		var scene := Node3D.new()
		viewport.add_child(scene)
		var environment := WorldEnvironment.new()
		environment.environment = Environment.new()
		environment.environment.background_mode = Environment.BG_COLOR
		environment.environment.background_color = Color("242831")
		environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
		environment.environment.ambient_light_color = Color("e7edf5")
		environment.environment.ambient_light_energy = 0.8
		environment.environment.tonemap_mode = Environment.TONE_MAPPER_LINEAR
		scene.add_child(environment)
		var light := DirectionalLight3D.new()
		light.rotation_degrees = Vector3(-38, -30, 0)
		light.light_energy = 0.65
		scene.add_child(light)
		var document := GLTFDocument.new()
		var state := GLTFState.new()
		assert(document.append_from_buffer(FileAccess.get_file_as_bytes(model.SourcePath + ".bytes"), "", state) == OK)
		var animal := document.generate_scene(state)
		var wrapper := Node3D.new()
		wrapper.scale = Vector3.ONE * model.Scale
		var minimum: Array = metrics.RawBoundsMin
		var maximum: Array = metrics.RawBoundsMax
		wrapper.position = Vector3(-(minimum[0] + maximum[0]) * .5, -minimum[1], -(minimum[2] + maximum[2]) * .5) * model.Scale
		scene.add_child(wrapper)
		wrapper.add_child(animal)
		for player in animal.find_children("*", "AnimationPlayer", true, false):
			player.play("Idle")
			player.seek(.65, true)
			player.advance(0)
		var height: float = metrics.TargetHeightMetres
		var extent := maxf(height, (maximum[2] - minimum[2]) * model.Scale)
		var camera := Camera3D.new()
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = extent * 1.48
		camera.position = Vector3(3, 1.8, 4).normalized() * extent * 3 + Vector3.UP * height * .45
		scene.add_child(camera)
		camera.look_at(Vector3.UP * height * .45)
		camera.current = true
		var ground := MeshInstance3D.new()
		var plane := PlaneMesh.new()
		plane.size = Vector2(20, 20)
		ground.mesh = plane
		var material := StandardMaterial3D.new()
		material.albedo_color = Color("363c47")
		material.roughness = .9
		ground.material_override = material
		scene.add_child(ground)
		var label := Label.new()
		label.text = model.Name + "  ·  " + str(int(metrics.AnimationCount)) + " clips"
		label.position = Vector2(14, 12)
		label.add_theme_font_size_override("font_size", 16)
		viewport.add_child(label)
	for i in range(6):
		await process_frame
	await RenderingServer.frame_post_draw
	var output := Image.create(1280, 900, false, Image.FORMAT_RGBA8)
	for i in range(viewports.size()):
		var pixels := viewports[i].get_texture().get_image()
		output.blit_rect(pixels, Rect2i(0, 0, 320, 300), Vector2i((i % 4) * 320, (i / 4) * 300))
	assert(output.save_png("res://assets/content/animals/preview.png") == OK)
	print("ANIMAL_PREVIEW saved")
	quit()
