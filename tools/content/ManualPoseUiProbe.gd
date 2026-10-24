extends SceneTree
func nodes(node):
 var result=[node]
 for child in node.get_children(): result.append_array(nodes(child))
 return result
func _initialize(): call_deferred('run')
func run():
 create_timer(15).timeout.connect(func(): push_error('MANUAL_POSE_UI_TIMEOUT'); quit(1))
 var app=load('res://Main.tscn').instantiate()
 root.add_child(app)
 for i in range(5): await process_frame
 var scene_list=nodes(app).filter(func(n): return n is ItemList)[0]
 var figure_index=-1
 for i in range(scene_list.item_count):
  if scene_list.get_item_text(i)=='Seated figure': figure_index=i
 assert(figure_index>=0,'Seated example figure missing')
 scene_list.select(figure_index)
 scene_list.item_selected.emit(figure_index)
 var poses=nodes(app).filter(func(n): return n is Button and n.text=='Poses')[0]
 poses.pressed.emit()
 var mode=nodes(app).filter(func(n): return n is Button and n.text=='Pose' and n.toggle_mode)[0]
 mode.pressed.emit()
 for i in range(4): await process_frame
 var surfaces=nodes(app).filter(func(n): return n is SubViewportContainer and n.get_children().any(func(c): return c is SubViewport and c.has_node('World')))
 assert(surfaces.size()==1,'Scene viewport missing')
 var surface=surfaces[0]
 var view=surface.get_children().filter(func(n): return n is SubViewport)[0]
 var camera=view.get_camera_3d()
 var actor=nodes(view.get_node('World')).filter(func(n): return n is Node3D and n.has_node('PreparedHumanoid') and n.global_position.x < -1 and n.global_position.x > -1.3)[0]
 var skeleton=actor.get_node('PreparedHumanoid')
 var attachment=skeleton.get_children().filter(func(n): return n is BoneAttachment3D and n.bone_name=='RightHand')[0]
 var mesh=attachment.get_children().filter(func(n): return n is MeshInstance3D and n.mesh is CapsuleMesh)[0]
 var bounds=mesh.get_aabb()
 var world=mesh.global_transform*(bounds.get_center()+Vector3.UP*bounds.size.y*.32)
 var point=camera.unproject_position(world)*surface.size/Vector2(view.size)
 var names=['RightUpperArm','RightLowerArm','RightHand']
 var before=names.map(func(n): return skeleton.get_bone_pose_rotation(skeleton.find_bone(n)))
 var original_transform=actor.transform
 RenderingServer.force_draw()
 root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/manual-pose-ui-before.png')
 var down=InputEventMouseButton.new();down.button_index=MOUSE_BUTTON_LEFT;down.pressed=true;down.position=point
 surface.gui_input.emit(down)
 for i in range(16):
  var motion=InputEventMouseMotion.new();motion.position=point+Vector2(50,-32)*(i+1)/16.;motion.relative=Vector2(50,-32)/16.;surface.gui_input.emit(motion)
  await process_frame
 var up=InputEventMouseButton.new();up.button_index=MOUSE_BUTTON_LEFT;up.pressed=false;up.position=point+Vector2(50,-32);surface.gui_input.emit(up)
 for i in range(3): await process_frame
 var after=names.map(func(n): return skeleton.get_bone_pose_rotation(skeleton.find_bone(n)))
 var changed=[]
 for i in range(3):
  changed.append(abs(before[i].dot(after[i]))<.99999)
 assert(changed.all(func(v): return v),'Actual hand drag did not update every arm joint')
 assert(actor.transform.is_equal_approx(original_transform),'Pose drag moved the whole figure')
 var head_index=skeleton.find_bone('Head')
 var head_before=skeleton.get_bone_pose_rotation(head_index)
 var head_attachment=skeleton.get_children().filter(func(n): return n is BoneAttachment3D and n.bone_name=='Head')[0]
 var head_mesh=head_attachment.get_children().filter(func(n): return n is MeshInstance3D and n.mesh is SphereMesh)[0]
 var head_point=camera.unproject_position(head_mesh.global_transform*head_mesh.get_aabb().get_center())*surface.size/Vector2(view.size)
 down.position=head_point;surface.gui_input.emit(down)
 var turn=InputEventMouseMotion.new();turn.position=head_point+Vector2(18,-9);turn.relative=Vector2(18,-9);surface.gui_input.emit(turn)
 up.position=turn.position;surface.gui_input.emit(up)
 for i in range(2): await process_frame
 assert(abs(head_before.dot(skeleton.get_bone_pose_rotation(head_index)))<.99999,'Head mesh drag did not turn its joint')
 # Reselect the hand without moving it so the final view shows the arm control.
 point=camera.unproject_position(mesh.global_transform*(bounds.get_center()+Vector3.UP*bounds.size.y*.32))*surface.size/Vector2(view.size)
 down.position=point;surface.gui_input.emit(down);up.position=point;surface.gui_input.emit(up)
 RenderingServer.force_draw()
 var path='C:/Users/Petr/Documents/Previs/output/manual-pose-ui-after.png'
 root.get_texture().get_image().save_png(path)
 print('MANUAL_POSE_UI_OK actualPalm=true changedJoints=',changed,' wholeFigureFixed=true headTurn=true screenshot=',path)
 app.queue_free();await process_frame;quit(0)
