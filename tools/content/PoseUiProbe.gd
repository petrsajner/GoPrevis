extends SceneTree
func nodes(node):
 var result=[node]
 for child in node.get_children(): result.append_array(nodes(child))
 return result
func _initialize():
 print('POSE_UI initialized')
 call_deferred('run')
func run():
 print('POSE_UI loading scene')
 create_timer(15).timeout.connect(func(): push_error('POSE_UI_TIMEOUT'); quit(1))
 var app=load('res://Main.tscn').instantiate()
 root.add_child(app)
 print('POSE_UI app ready')
 for i in range(6): await process_frame
 var tabs=nodes(app).filter(func(n): return n is Button and n.text=='Poses')
 assert(tabs.size()==1,'Poses library tab missing')
 tabs[0].pressed.emit()
 print('POSE_UI tab opened')
 for i in range(3): await process_frame
 var searches=nodes(app).filter(func(n): return n is LineEdit and n.placeholder_text=='Search poses…')
 assert(searches.size()==1,'Pose search missing')
 var search=searches[0]
 search.text='Sit upright'
 search.text_changed.emit(search.text)
 for i in range(3): await process_frame
 assert(nodes(app).any(func(n): return n is Label and n.text=='1 items · drag into scene'),'Pose search did not narrow actual library')
 search.text=''
 search.text_changed.emit(search.text)
 for i in range(3): await process_frame
 assert(nodes(app).any(func(n): return n is Label and n.text=='118 items · drag into scene'),'All pose cards are not discoverable')
 print('POSE_UI forcing render')
 RenderingServer.force_draw()
 var destination='C:/Users/Petr/Documents/Previs/output/pose-library-ui.png'
 root.get_texture().get_image().save_png(destination)
 print('POSE_UI_OK search=1 all=118 screenshot=',destination)
 app.queue_free()
 await process_frame
 quit(0)
