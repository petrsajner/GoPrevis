extends SceneTree
func _initialize():
 var failures=[]
 var sources={}
 var covers={}
 for file in ['animal-models','prop-models','human-motions','animal-motions']:
  var path='res://assets/content/manifests/'+file+'.json'
  if not FileAccess.file_exists(path):
   failures.append(path)
   continue
  for entry in JSON.parse_string(FileAccess.get_file_as_string(path)):
   var source=entry.SourcePath
   sources[source]=true
   covers[entry.Id]=true
   if not FileAccess.file_exists(source) and not FileAccess.file_exists(source+'.bytes'): failures.append(source)
   var cover='res://assets/content/thumbnails/'+entry.Id+'.png'
   if not ResourceLoader.exists(cover): failures.append(cover)
 for id in ['human-average','human-broad','human-slim','human-tall','human-youth','human-child','fox_actor','box','sphere','cylinder','wall','floor','table','chair','car','tree','dog','human-walk-gesture','human-cmu-walk','fox-walk','fox-run','fox-survey']:
  var cover='res://assets/content/thumbnails/'+id+'.png'
  covers[id]=true
  if not ResourceLoader.exists(cover): failures.append(cover)
 var poses=JSON.parse_string(FileAccess.get_file_as_string('res://assets/content/manifests/human-poses.json'))
 for pose in poses:
  covers[pose.Id]=true
  if pose.Pose.BoneRotations.size()!=21: failures.append(pose.Id+' bones')
  if not ResourceLoader.exists('res://assets/content/thumbnails/'+pose.Id+'.png'): failures.append(pose.Id+' cover')
 if poses.size()!=118: failures.append('pose count')
 print('PACKAGED_CONTENT_INVENTORY sources=',sources.size(),' covers=',covers.size(),' poses=',poses.size(),' failures=',failures)
 quit(0 if failures.is_empty() else 1)
