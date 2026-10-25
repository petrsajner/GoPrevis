extends SceneTree
func nodes(n):
 var a=[n]
 for c in n.get_children():a.append_array(nodes(c))
 return a
func button(app,text):
 var found=nodes(app).filter(func(n):return n is Button and n.text==text and n.is_visible_in_tree())
 assert(found.size()==1,'Visible button missing or ambiguous: '+text)
 return found[0]
func settle(count=4):
 for i in range(count):await process_frame
func run():
 create_timer(25).timeout.connect(func():push_error('CAMERA_BACKGROUND_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app);await settle(6)
 var output='C:/Users/Petr/Documents/Previs/output/camera-background-ui'
 DirAccess.make_dir_recursive_absolute(output)
 var image=Image.create(64,64,false,Image.FORMAT_RGB8);image.fill(Color(1,0,0));image.save_png(output+'/camera-a.png');image.fill(Color(0,0,1));image.save_png(output+'/camera-b.png')
 var quick=button(app,'Camera background…');assert(quick.visible,'Camera background action is always visible below preview');quick.pressed.emit();await settle()
 var modal=nodes(app).filter(func(n):return n is AcceptDialog and n.title=='Camera A · Camera background')[0]
 assert(modal.visible and modal.size.x<=500 and modal.size.y<=400,'Camera background modal should stay compact')
 button(modal,'Load image / video…').pressed.emit();await settle()
 var picker=nodes(modal).filter(func(n):return n is FileDialog)[0];assert(picker.visible and picker.get_parent()==modal,'Picker must be nested in the active camera modal')
 picker.file_selected.emit(output+'/camera-a.png');await settle();modal.hide()
 var scene=nodes(app).filter(func(n):return n is ItemList)[0]
 var camera_b=-1;var camera_a=-1
 for i in range(scene.item_count):
  if scene.get_item_text(i)=='[Camera] Camera B':camera_b=i
  if scene.get_item_text(i)=='[Camera] Camera A':camera_a=i
 assert(camera_b>=0 and camera_a>=0,'Both cameras must be selectable scene objects')
 scene.select(camera_b);scene.item_selected.emit(camera_b);await settle()
 var headers=nodes(app).filter(func(n):return n is Button and n.text.begins_with('▸ Camera background ·') and n.is_visible_in_tree())
 assert(headers.size()==1 and headers[0].text.ends_with('None'),'Camera B must start with its own empty background')
 headers[0].pressed.emit();await settle();button(app,'Load image / video…').pressed.emit();await settle()
 var inspectors=nodes(app).filter(func(n):return n is FileDialog and n.title=='Choose background for Camera B')[0];inspectors.file_selected.emit(output+'/camera-b.png');await settle()
 var selected_headers=nodes(app).filter(func(n):return n is Button and n.text.begins_with('▾ Camera background ·') and n.is_visible_in_tree())
 assert(selected_headers.size()==1 and selected_headers[0].text.ends_with('camera-b.png'),'The camera inspector should identify its image')
 RenderingServer.force_draw();root.get_texture().get_image().save_png(output+'/camera-b-inspector.png')
 scene.select(camera_a);scene.item_selected.emit(camera_a);await settle()
 selected_headers=nodes(app).filter(func(n):return n is Button and n.text.begins_with('▾ Camera background ·') and n.is_visible_in_tree())
 assert(selected_headers[0].text.ends_with('camera-a.png'),'Camera A must retain its original independent source')
 RenderingServer.force_draw();root.get_texture().get_image().save_png(output+'/camera-a-inspector.png')
 print('CAMERA_BACKGROUND_UI_OK quickAction=true compactModal=true nestedPicker=true inspectorFoldout=true independentCameras=true')
 app.queue_free();await process_frame;quit(0)
func _initialize():call_deferred('run')
