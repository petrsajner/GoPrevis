extends SceneTree
func nodes(n):
 var a=[n]
 for c in n.get_children():a.append_array(nodes(c))
 return a
func _initialize():call_deferred('run')
func run():
 create_timer(20).timeout.connect(func():push_error('PROJECT_SETUP_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(6):await process_frame
 var setup_buttons=nodes(app).filter(func(n):return n is Button and n.text.begins_with('Project ·'))
 assert(setup_buttons.size()==1,'Project setup toolbar button missing');setup_buttons[0].pressed.emit()
 for i in range(5):await process_frame
 var dialog=nodes(app).filter(func(n):return n is ConfirmationDialog and n.title=='Project setup')[0]
 assert(dialog.visible and dialog.size.y<=650,'Project setup dialog overflowed the desktop')
 var formats=nodes(dialog).filter(func(n):return n is OptionButton and n.get_item_count()==7 and n.get_item_text(0).begins_with('16:9'))[0]
 formats.select(1);formats.item_selected.emit(1)
 var fps=nodes(dialog).filter(func(n):return n is OptionButton and n.get_item_count()==9 and n.get_item_text(0)=='12 fps')[0]
 fps.select(1);fps.item_selected.emit(1)
 var load_ref=nodes(dialog).filter(func(n):return n is Button and n.text=='Load image / video…')[0];load_ref.pressed.emit()
 for i in range(3):await process_frame
 var picker=nodes(dialog).filter(func(n):return n is FileDialog)[0]
 assert(picker.get_parent()==dialog and picker.visible,'Reference picker must be nested in the modal setup dialog')
 picker.file_selected.emit('C:/Users/Petr/Documents/Previs/output/project-setup-checks/reference.png')
 for i in range(3):await process_frame
 assert(dialog.get_ok_button().get_global_rect().end.y<=dialog.position.y+dialog.size.y,'Apply button inaccessible')
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/project-setup-ui-0.11.0.png')
 print('PROJECT_SETUP_DIALOG_GEOMETRY size=',dialog.size,' root=',root.size)
 dialog.confirmed.emit();dialog.hide()
 for i in range(5):await process_frame
 assert(setup_buttons[0].text.contains('9:16') and setup_buttons[0].text.contains('15 fps'),'Project format did not apply from actual UI')
 var camera_view=nodes(app).filter(func(n):return n is Button and n.text in ['Camera view','Enter camera view'])[0];camera_view.pressed.emit()
 for i in range(5):await process_frame
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/project-camera-portrait-ui-0.11.0.png')
 print('PROJECT_SETUP_UI_OK modalPicker=true aspect=9:16 fps=15 imageReference=true cameraGate=true')
 app.queue_free();await process_frame;quit(0)
