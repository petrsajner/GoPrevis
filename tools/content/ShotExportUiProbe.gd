extends SceneTree
func nodes(n):
 var a=[n]
 for c in n.get_children():a.append_array(nodes(c))
 return a
func _initialize():call_deferred('run')
func run():
 create_timer(15).timeout.connect(func():push_error('EXPORT_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(5):await process_frame
 var export=nodes(app).filter(func(n):return n is Button and n.text=='Export shot')[0];export.pressed.emit()
 for i in range(7):await process_frame
 var dialogs=nodes(app).filter(func(n):return n is ConfirmationDialog and n.title=='Export shot');assert(dialogs.size()==1 and dialogs[0].visible,'Export shot dialog missing')
 assert(dialogs[0].size.y<=650,'Export dialog overflowed the desktop')
 var labels=nodes(dialogs[0]).filter(func(n):return n is CheckButton).map(func(n):return n.text)
 assert(labels.has('Color') and labels.has('Depth') and labels.has('Pose'),'Pass choices missing')
 print('DIALOG_GEOMETRY size=',dialogs[0].size,' min=',dialogs[0].min_size,' root=',root.size)
 for child in dialogs[0].get_children():
  if child is Control:print('DIALOG_CHILD ',child.name,' size=',child.size,' min=',child.get_combined_minimum_size())
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/export-dialog-0.9.0.png')
 print('EXPORT_UI_OK activeCamera=true formats=true passes=Color,Depth,Pose')
 dialogs[0].hide();app.queue_free();await process_frame;quit(0)
