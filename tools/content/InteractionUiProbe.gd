extends SceneTree
func nodes(node):
 var result=[node]
 for child in node.get_children():result.append_array(nodes(child))
 return result
func _initialize():call_deferred('run')
func run():
 create_timer(15).timeout.connect(func():push_error('INTERACTION_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(6):await process_frame
 var surface=nodes(app).filter(func(n):return n is SubViewportContainer and n.get_children().any(func(c):return c is SubViewport and c.has_node('World')))[0]
 var view=surface.get_children().filter(func(n):return n is SubViewport)[0];var camera=view.get_camera_3d();var world=view.get_node('World')
 var document=JSON.parse_string(FileAccess.get_file_as_string('res://samples/manual-adjusted-pose.genprevis'))
 var pos=document.Cameras[0].Position;var expected=Vector3(pos[0],pos[1],pos[2])
 var rigs=world.get_children().filter(func(n):return n is Node3D and n.has_node('CameraBody'))
 rigs.sort_custom(func(a,b):return a.global_position.distance_to(expected)<b.global_position.distance_to(expected))
 var rig=rigs[0];var mesh=rig.get_node('CameraBody');var point=camera.unproject_position(mesh.global_transform*mesh.get_aabb().get_center())*surface.size/Vector2(view.size)
 var down=InputEventMouseButton.new();down.button_index=MOUSE_BUTTON_LEFT;down.pressed=true;down.position=point;surface.gui_input.emit(down)
 var up=InputEventMouseButton.new();up.button_index=MOUSE_BUTTON_LEFT;up.pressed=false;up.position=point;surface.gui_input.emit(up)
 var scene_list=nodes(app).filter(func(n):return n is ItemList)[0];assert(scene_list.get_item_text(scene_list.get_selected_items()[0]).contains('Camera A'),'camera click failed')
 var hover=InputEventMouseMotion.new();hover.position=Vector2(20,20);hover.shift_pressed=true;surface.gui_input.emit(hover)
 for i in range(2):await process_frame
 var origin=rig.global_position;var screen=camera.unproject_position(origin);var depth=maxf(.05,-(camera.global_transform.affine_inverse()*origin).z)
 var length=camera.project_position(screen+Vector2(92*view.size.x/surface.size.x,0),depth).distance_to(origin)
 point=camera.unproject_position(origin+Vector3.UP*length*.95)*surface.size/Vector2(view.size)
 var destination=camera.unproject_position(origin+Vector3.UP*(length*.95+.35))*surface.size/Vector2(view.size)
 down.position=point;down.shift_pressed=true;surface.gui_input.emit(down)
 var motion=InputEventMouseMotion.new();motion.position=destination;motion.relative=destination-point;motion.shift_pressed=true;surface.gui_input.emit(motion)
 up.position=destination;up.shift_pressed=true;surface.gui_input.emit(up)
 for i in range(3):await process_frame
 assert(abs(rig.global_position.y-origin.y-.35)<.003,'camera Y gizmo failed')
 var number=nodes(app).filter(func(n):return n is SpinBox and n.tooltip_text=='Position Y')[0];var before=number.value;var arrows=number.get_node('PrecisionArrows')
 down.position=Vector2(8,15);down.shift_pressed=false;arrows.gui_input.emit(down)
 motion.position=Vector2(8,5);motion.relative=Vector2(0,-10);motion.shift_pressed=false;arrows.gui_input.emit(motion)
 motion.position=Vector2(8,9);motion.relative=Vector2(0,4);arrows.gui_input.emit(motion)
 up.position=Vector2(8,9);up.shift_pressed=false;arrows.gui_input.emit(up)
 for i in range(3):await process_frame
 assert(abs(number.value-before-.006)<.0001,'precision arrows failed')
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/interaction-ui-0.7.0.png')
 print('INTERACTION_UI_OK cameraPicked=true cameraLift=.35 precisionScrub=.006 shiftGizmo=true')
 app.queue_free();await process_frame;quit(0)
