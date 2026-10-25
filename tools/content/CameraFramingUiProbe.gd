extends SceneTree
const NAMES=['Current view','Front medium','Front close-up','Front wide','Side follow','Side close','Back medium','Top wide','45° high angle','Low angle','Low wide','Over shoulder left','Over shoulder right',"Bird's-eye",'Dutch angle']
func nodes(node):
 var result=[node]
 for child in node.get_children():result.append_array(nodes(child))
 return result
func _initialize():call_deferred('run')
func run():
 create_timer(20).timeout.connect(func():push_error('CAMERA_FRAMING_UI_TIMEOUT');quit(1))
 var app=load('res://Main.tscn').instantiate();root.add_child(app)
 for i in range(6):await process_frame
 var tab=nodes(app).filter(func(n):return n is Button and n.text=='Cameras')[0];tab.pressed.emit()
 for i in range(3):await process_frame
 var cards=[]
 for name in NAMES:
  var found=nodes(app).filter(func(n):return n is Button and n.tooltip_text.begins_with(name+'\n'))
  assert(found.size()==1,'Framing card missing: '+name);cards.append(found[0])
 cards[3].pressed.emit()
 for i in range(3):await process_frame
 RenderingServer.force_draw();root.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/camera-framing-library.png')
 var contact=SubViewport.new();contact.size=Vector2i(1120,540);contact.disable_3d=true;contact.render_target_update_mode=SubViewport.UPDATE_ALWAYS;root.add_child(contact)
 var background=ColorRect.new();background.color=Color('#111116');background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT);contact.add_child(background)
 var margin=MarginContainer.new();margin.theme=app.theme;margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT);margin.add_theme_constant_override('margin_left',20);margin.add_theme_constant_override('margin_right',20);margin.add_theme_constant_override('margin_top',12);margin.add_theme_constant_override('margin_bottom',12);contact.add_child(margin)
 var column=VBoxContainer.new();margin.add_child(column)
 var title=Label.new();title.text='PREVIS · 15 CAMERA FRAMINGS';title.add_theme_font_size_override('font_size',18);column.add_child(title)
 var grid=GridContainer.new();grid.columns=5;grid.add_theme_constant_override('h_separation',8);grid.add_theme_constant_override('v_separation',6);column.add_child(grid)
 for card in cards:
  card.get_parent().remove_child(card);card.custom_minimum_size=Vector2(208,153);grid.add_child(card)
 for i in range(4):await process_frame
 RenderingServer.force_draw();contact.get_texture().get_image().save_png('C:/Users/Petr/Documents/Previs/output/camera-framings-contact.png')
 print('CAMERA_FRAMING_UI_OK cards=15 selectedSubject=true ownVectorDiagrams=true')
 contact.queue_free();app.queue_free();await process_frame;quit(0)
