extends SceneTree
func nodes(n):
 var all=[n]
 for child in n.get_children():all.append_array(nodes(child))
 return all
func _initialize():call_deferred('run')
func run():
 create_timer(15).timeout.connect(func():push_error('KEYED_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(6):await process_frame
 var buttons=nodes(app).filter(func(n):return n is Button)
 assert(not buttons.any(func(n):return n.text=='Select'),'separateSelecttoolstillpresent')
 var surface=nodes(app).filter(func(n):return n is SubViewportContainer and n.get_children().any(func(c):return c is SubViewport and c.has_node('World')))[0]
 var view=surface.get_children().filter(func(n):return n is SubViewport)[0];var world=view.get_node('World');var camera=view.get_camera_3d()
 var actor=world.get_children().filter(func(n):return n is Node3D and n.has_node('PreparedHumanoid'))[0];var skel=actor.get_node('PreparedHumanoid');var slider=nodes(app).filter(func(n):return n is HSlider)[0]
 var source_head_zero=skel.get_bone_pose_rotation(skel.find_bone('Head'));var leg_zero=skel.get_bone_pose_rotation(skel.find_bone('LeftUpperLeg'))
 slider.value=2
 var pose_button=buttons.filter(func(n):return n.text=='Pose' and n.toggle_mode)[0];pose_button.pressed.emit()
 for i in range(4):await process_frame
 var head_attachment=skel.get_children().filter(func(n):return n is BoneAttachment3D and n.bone_name=='Head')[0]
 var head_mesh=head_attachment.get_children().filter(func(n):return n is MeshInstance3D and n.mesh is SphereMesh)[0]
 var point=camera.unproject_position(head_mesh.global_transform*head_mesh.get_aabb().get_center())*surface.size/Vector2(view.size)
 var down=InputEventMouseButton.new();down.button_index=MOUSE_BUTTON_LEFT;down.pressed=true;down.position=point;surface.gui_input.emit(down)
 var motion=InputEventMouseMotion.new();motion.position=point+Vector2(85,-9);motion.relative=Vector2(85,-9);surface.gui_input.emit(motion)
 var up=InputEventMouseButton.new();up.button_index=MOUSE_BUTTON_LEFT;up.pressed=false;up.position=motion.position;surface.gui_input.emit(up)
 for i in range(3):await process_frame
 var corrected_head=skel.get_bone_pose_rotation(skel.find_bone('Head'))
 slider.value=0
 for i in range(2):await process_frame
 assert(abs(source_head_zero.dot(skel.get_bone_pose_rotation(skel.find_bone('Head'))))>.99999,'identitykeydoesnotrestoresource')
 slider.value=.7
 for i in range(2):await process_frame
 assert(abs(leg_zero.dot(skel.get_bone_pose_rotation(skel.find_bone('LeftUpperLeg'))))<.9999,'sourceleganimationwasremoved')
 slider.value=2
 for i in range(2):await process_frame
 assert(abs(corrected_head.dot(skel.get_bone_pose_rotation(skel.find_bone('Head'))))>.99999,'keyedheadturnnotrestored')
 slider.value=3
 for i in range(3):await process_frame
 var frame_button=buttons.filter(func(n):return n.text=='Frame')[0];frame_button.pressed.emit()
 for i in range(2):await process_frame
 var hand_attachment=skel.get_children().filter(func(n):return n is BoneAttachment3D and n.bone_name=='RightHand')[0]
 var hand_mesh=hand_attachment.get_children().filter(func(n):return n is MeshInstance3D and n.mesh is CapsuleMesh)[0]
 var hand_box=hand_mesh.get_aabb()
 point=camera.unproject_position(hand_mesh.global_transform*(hand_box.get_center()+Vector3.UP*hand_box.size.y*.25))*surface.size/Vector2(view.size)
 var hand_before=(actor.global_transform*skel.get_bone_global_pose(skel.find_bone('RightHand')).origin).y
 down.position=point;surface.gui_input.emit(down)
 motion.position=point+Vector2(-8,-120);motion.relative=Vector2(-8,-120);surface.gui_input.emit(motion)
 up.position=motion.position;surface.gui_input.emit(up)
 for i in range(3):await process_frame
 assert((actor.global_transform*skel.get_bone_global_pose(skel.find_bone('RightHand')).origin).y>hand_before+.1,'hand was not raised above mocap')
 var timeline=nodes(app).filter(func(n):return n.has_method('ExpandPoseCorrections'))
 if timeline.size()>0:timeline[0].call('ExpandPoseCorrections',str(actor.get_meta('entry_id')))
 if app.has_method('SaveTo'):app.call('SaveTo','C:/Users/Petr/Documents/Previs/output/keyed-ui-demo.previs')
 for i in range(3):await process_frame
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/unified-keyed-ui-0.8.0.png')
 print('KEYED_UI_OK unifiedArrange=true mocapContinues=true headKeyRestores=true handRaised=true expandedPoseTimeline=',timeline.size()>0)
 app.queue_free();await process_frame;quit(0)
