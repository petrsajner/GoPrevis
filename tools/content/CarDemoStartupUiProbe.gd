extends SceneTree

func nodes(node):
 var result=[node]
 for child in node.get_children():result.append_array(nodes(child))
 return result

func _initialize():call_deferred('run')

func run():
 create_timer(20).timeout.connect(func():push_error('CAR_DEMO_STARTUP_UI_TIMEOUT');quit(1))
 assert(OS.get_cmdline_user_args().has('--demo=car-chase'),'The probe must exercise the actual demo startup argument')
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(6):await process_frame
 var camera_views=nodes(app).filter(func(node):return node is Button and node.text=='Exit camera view' and node.is_visible_in_tree())
 assert(camera_views.size()==1,'The actual startup argument must open camera view immediately')
 var camera_choices=nodes(app).filter(func(node):return node is OptionButton and node.get_item_count()>0 and node.get_selected()>=0 and node.get_item_text(node.get_selected())=='Car follow')
 assert(camera_choices.size()==1,'Car follow must be the active camera')
 var backgrounds=nodes(app).filter(func(node):return node is Button and node.text=='Camera background…' and node.is_visible_in_tree())
 assert(backgrounds.size()==1,'The background action must be available immediately')
 RenderingServer.force_draw();DirAccess.make_dir_recursive_absolute('res://output');root.get_texture().get_image().save_png('res://output/car-demo-startup-ui.png')
 var key=InputEventKey.new();key.keycode=KEY_SPACE;key.pressed=true;Input.parse_input_event(key)
 for i in range(3):await process_frame
 assert(nodes(app).any(func(node):return node is Button and node.text.contains('Pause')),'Space must start playback after opening the demo')
 print('CAR_DEMO_STARTUP_UI_OK argument=true cameraView=true followCamera=true backgroundAction=true spaceToPlay=true')
 app.queue_free();await process_frame;quit(0)
