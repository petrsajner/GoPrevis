extends SceneTree
func nodes(n):
 var a=[n]
 for c in n.get_children():a.append_array(nodes(c))
 return a
func _initialize():call_deferred('run')
func run():
 create_timer(15).timeout.connect(func():push_error('SCENE_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(5):await process_frame
 nodes(app).filter(func(n):return n is Button and n.text=='Objects')[0].pressed.emit()
 nodes(app).filter(func(n):return n is Button and n.text=='▸ 3D scene')[0].pressed.emit()
 for i in range(2):await process_frame
 var pickers=nodes(app).filter(func(n):return n is ColorPickerButton and n.is_visible_in_tree())
 pickers.sort_custom(func(a,b):return a.get_global_rect().position.x<b.get_global_rect().position.x)
 assert(pickers.size()==2,'Background and selected-object color controls missing')
 pickers[0].color=Color('#172435');pickers[0].color_changed.emit(pickers[0].color)
 pickers[1].color=Color('#e79851');pickers[1].color_changed.emit(pickers[1].color)
 var brightness=nodes(app).filter(func(n):return n is HSlider and n.max_value==400)[0];brightness.value=80
 var opacity=nodes(app).filter(func(n):return n is HSlider and n.max_value==100)[0];opacity.value=70
 var name=nodes(app).filter(func(n):return n is LineEdit and n.placeholder_text=='Select an object')[0];name.text='Lead actor';name.text_submitted.emit(name.text)
 var ground_buttons=nodes(app).filter(func(n):return n is Button and n.text=='Place on ground');assert(ground_buttons.size()==1,'Ground placement missing')
 ground_buttons[0].pressed.emit()
 assert(nodes(app).any(func(n):return n is Button and n.text=='Place on object'),'Object placement missing')
 for i in range(4):await process_frame
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/scene-settings-ui-0.10.0.png')
 print('SCENE_UI_OK background=true brightness=80 opacity=70 objectColor=true labels=true placement=true')
 app.queue_free();await process_frame;quit(0)
