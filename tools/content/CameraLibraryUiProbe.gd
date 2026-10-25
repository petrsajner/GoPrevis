extends SceneTree
const NAMES=['Static shot','Tilt up','Tilt down','Pan left','Pan right','Crane up','Crane down','Truck left','Truck right','Dolly in','Dolly out','Zoom in','Zoom out','Handheld','Slider reveal','Push through','Whip pan','Crash zoom','Orbit','Ascending orbit','Descending orbit','Aerial establishing','Dive','Pull away']
func nodes(node):
 var result=[node]
 for child in node.get_children():result.append_array(nodes(child))
 return result
func _initialize():call_deferred('run')
func run():
 create_timer(20).timeout.connect(func():push_error('CAMERA_LIBRARY_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(6):await process_frame
 var tab=nodes(app).filter(func(n):return n is Button and n.text=='Cameras')
 assert(tab.size()==1,'Cameras library tab missing');tab[0].pressed.emit()
 for i in range(3):await process_frame
 var cards=[]
 for name in NAMES:
  var found=nodes(app).filter(func(n):return n is Button and n.tooltip_text.begins_with(name+'\n'))
  assert(found.size()==1,'Preset card missing or duplicated: '+name)
  cards.append(found[0])
 var scene_surface=nodes(app).filter(func(n):return n is SubViewportContainer and n.get_children().any(func(c):return c is SubViewport and c.has_node('World')))[0]
 var scene_view=scene_surface.get_children().filter(func(n):return n is SubViewport)[0]
 var rig=scene_view.get_node('World').get_children().filter(func(n):return n is Node3D and n.has_node('CameraBody'))[0]
 var shot=rig.get_node('ShotCamera');var before=rig.global_transform;var initial_fov=shot.fov
 cards[11].pressed.emit()
 var slider=nodes(app).filter(func(n):return n is HSlider)[0];slider.value=slider.max_value
 for i in range(3):await process_frame
 assert(shot.fov<initial_fov-1 and rig.global_transform.is_equal_approx(before),'Zoom card did not animate lens independently')
 slider.value=0
 for i in range(3):await process_frame
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/camera-library-0.9.0.png')
 # Reparent the original live vector cards for a full contact sheet; no mocked icons.
 var contact=SubViewport.new();contact.size=Vector2i(1120,680);contact.disable_3d=true;contact.render_target_update_mode=SubViewport.UPDATE_ALWAYS;root.add_child(contact)
 var background=ColorRect.new();background.color=Color('#111116');background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT);contact.add_child(background)
 var margin=MarginContainer.new();margin.theme=app.theme;margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT);margin.add_theme_constant_override('margin_left',20);margin.add_theme_constant_override('margin_right',20);margin.add_theme_constant_override('margin_top',12);margin.add_theme_constant_override('margin_bottom',12);contact.add_child(margin)
 var column=VBoxContainer.new();margin.add_child(column)
 var title=Label.new();title.text='GOPREVIS · 24 CAMERA MOVEMENTS';title.add_theme_font_size_override('font_size',18);column.add_child(title)
 var grid=GridContainer.new();grid.columns=6;grid.add_theme_constant_override('h_separation',6);grid.add_theme_constant_override('v_separation',6);column.add_child(grid)
 for card in cards:
  card.get_parent().remove_child(card);card.custom_minimum_size=Vector2(174,143);grid.add_child(card)
 for i in range(4):await process_frame
 RenderingServer.force_draw();contact.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/camera-moves-contact-0.9.0.png')
 print('CAMERA_LIBRARY_UI_OK cards=',cards.size(),' actualZoomLens=true currentViewPreserved=true diagrams=24')
 contact.queue_free();app.queue_free();await process_frame;quit(0)
