extends SceneTree

func nodes(node):
 var result=[node]
 for child in node.get_children():result.append_array(nodes(child))
 return result

func settle(count=4):
 for i in range(count):await process_frame

func _initialize():call_deferred('run')

func run():
 create_timer(25).timeout.connect(func():push_error('CAMERA_TRACKING_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app);await settle(6)
 var scene=nodes(app).filter(func(node):return node is ItemList)[0]
 var car=-1;var camera=-1
 for i in range(scene.item_count):
  if scene.get_item_text(i)=='Car':car=i
  if scene.get_item_text(i)=='[Camera] Camera B':camera=i
 assert(car>=0 and camera>=0,'The source scene must contain the car and a selectable camera')
 scene.select(car);scene.item_selected.emit(car);await settle()
 scene.select(camera);scene.item_selected.emit(camera);await settle()
 var headers=nodes(app).filter(func(node):return node is Button and node.text.begins_with('▸ Follow / Look at') and node.is_visible_in_tree())
 assert(headers.size()==1,'Camera properties must expose a compact Follow / Look at header')
 headers[0].pressed.emit();await settle()
 var follow=nodes(app).filter(func(node):return node is CheckButton and node.text=='Follow' and node.is_visible_in_tree())[0]
 var look=nodes(app).filter(func(node):return node is CheckButton and node.text=='Look at' and node.is_visible_in_tree())[0]
 follow.button_pressed=true;look.button_pressed=true;await settle()
 var targets=nodes(app).filter(func(node):return node is OptionButton and node.get_item_count()>0 and node.get_selected()>=0 and node.get_item_text(node.get_selected())=='Car' and node.is_visible_in_tree())
 assert(targets.size()==2,'Follow and Look at should independently select the previously chosen car')
 assert(nodes(app).any(func(node):return node is Button and node.text.begins_with('▾ Follow / Look at') and node.text.contains('Car')),'The live header should identify its target')
 assert(nodes(app).any(func(node):return node is Label and node.text.contains('Follow + Look at')),'The camera preview should identify live tracking instead of implying an empty animation')
 RenderingServer.force_draw();DirAccess.make_dir_recursive_absolute('res://output');root.get_texture().get_image().save_png('res://output/camera-tracking-ui-source.png')
 print('CAMERA_TRACKING_UI_OK properties=true follow=true lookAt=true targets=true liveStatus=true')
 app.queue_free();await process_frame;quit(0)
