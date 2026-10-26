extends SceneTree

# Offline conversion: calibrate global motion deltas against the publisher's T-pose.
# The resulting ordinary BVH uses the existing humanoid retargeter and timeline.
const INPUT = "res://output/library-expansion-2026-10-26/"
const OUTPUT = "res://assets/content/motions/human/ual/"
const MAP = [
	["Hips","pelvis",-1],["Spine","spine_01",0],["Chest","spine_03",1],["Neck","neck_01",2],["Head","Head",3],
	["LeftShoulder","clavicle_l",2],["LeftUpperArm","upperarm_l",5],["LeftLowerArm","lowerarm_l",6],["LeftHand","hand_l",7],
	["RightShoulder","clavicle_r",2],["RightUpperArm","upperarm_r",9],["RightLowerArm","lowerarm_r",10],["RightHand","hand_r",11],
	["LeftUpperLeg","thigh_l",0],["LeftLowerLeg","calf_l",13],["LeftFoot","foot_l",14],["LeftToe","ball_l",15],
	["RightUpperLeg","thigh_r",0],["RightLowerLeg","calf_r",17],["RightFoot","foot_r",18],["RightToe","ball_r",19]]

func descendants(node: Node) -> Array:
	var result: Array = [node]
	for child in node.get_children(): result.append_array(descendants(child))
	return result

func _initialize(): call_deferred("run")

func sample(player: AnimationPlayer,skeleton: Skeleton3D,name: String,time: float) -> Array:
	player.play(name)
	player.seek(time,true)
	player.advance(0)
	skeleton.force_update_all_bone_transforms()
	var transforms = []
	for joint in MAP:
		var index = skeleton.find_bone(joint[1])
		assert(index >= 0,"Missing source joint: " + joint[1])
		transforms.append(skeleton.global_transform*skeleton.get_bone_global_pose(index))
	return transforms

func hierarchy(index: int,rest: Array,depth: int,order: Array) -> String:
	order.append(index)
	var indent = "\t".repeat(depth)
	var offset: Vector3 = rest[index].origin if MAP[index][2]<0 else rest[index].origin-rest[MAP[index][2]].origin
	var text = indent + ("ROOT " if index==0 else "JOINT ") + MAP[index][0] + "\n" + indent + "{\n"
	text += indent + "\tOFFSET %.8f %.8f %.8f\n" % [offset.x,offset.y,offset.z]
	text += indent + ("\tCHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation\n" if index==0 else "\tCHANNELS 3 Zrotation Xrotation Yrotation\n")
	var children = []
	for child in MAP.size():
		if MAP[child][2]==index: children.append(child)
	for child in children: text += hierarchy(child,rest,depth+1,order)
	if children.is_empty():
		var tip: Vector3 = offset.normalized()*.08
		text += indent + "\tEnd Site\n" + indent + "\t{\n" + indent + "\t\tOFFSET %.8f %.8f %.8f\n"%[tip.x,tip.y,tip.z] + indent + "\t}\n"
	return text + indent + "}\n"

func category(name: String) -> String:
	if name.begins_with("Sitting") or name.begins_with("Driving"): return "Sitting"
	if "Farm_" in name or "Fixing" in name or "TreeChopping" in name: return "Work"
	if "Talking" in name or name in ["Interact","Consume","Yes","PickUp_Table"]: return "Interaction"
	if "Walk" in name or "Jog" in name or "Sprint" in name or "Crouch_Fwd" in name or "Swim" in name: return "Locomotion"
	if "Jump" in name or "Slide" in name or "Climb" in name or name=="Roll" or name=="LayToIdle": return "Parkour"
	if "Idle" in name: return "Idle"
	if "Pistol" in name or "Sword" in name or "Punch" in name or "Melee" in name or "Shield" in name or "Hit" in name: return "Combat"
	return "Action"

func run():
	DirAccess.make_dir_recursive_absolute(OUTPUT)
	var motions = []
	var audit = []
	for library in [1,2]:
		var doc = GLTFDocument.new()
		var state = GLTFState.new()
		assert(doc.append_from_file(INPUT+"UAL%d_Standard.glb"%library,state)==OK)
		var scene = doc.generate_scene(state)
		root.add_child(scene)
		var player: AnimationPlayer = descendants(scene).filter(func(n): return n is AnimationPlayer)[0]
		var skeleton: Skeleton3D = descendants(scene).filter(func(n): return n is Skeleton3D)[0]
		player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
		var rest = sample(player,skeleton,"A_TPose",0)
		var order = []
		var header = "HIERARCHY\n" + hierarchy(0,rest,0,order)
		for name in player.get_animation_list():
			if name in ["A_TPose","RESET"]: continue
			var animation = player.get_animation(name)
			var looped = name.ends_with("_Loop")
			var count = maxi(2,roundi(animation.length*30)+(0 if looped else 1))
			var filename = "UAL%d_%s.bvh" % [library,name]
			var file = FileAccess.open(OUTPUT+filename,FileAccess.WRITE)
			file.store_string(header + "MOTION\nFrames: %d\nFrame Time: 0.0333333333333333\n" % count)
			var maximum_rotation = 0.0
			for frame in count:
				var pose = sample(player,skeleton,name,minf(frame/30.0,animation.length))
				var deltas = []
				for joint in MAP.size():
					var delta: Basis = pose[joint].basis.orthonormalized()*rest[joint].basis.orthonormalized().inverse()
					deltas.append(delta)
				var values = PackedStringArray()
				for joint in order:
					if joint==0:
						var translation: Vector3 = pose[0].origin-rest[0].origin
						for value in [translation.x,translation.y,translation.z]: values.append("%.8f"%value)
					var local: Basis = deltas[joint] if MAP[joint][2]<0 else deltas[MAP[joint][2]].inverse()*deltas[joint]
					var angles = local.get_euler(EULER_ORDER_ZXY)*180.0/PI
					maximum_rotation = maxf(maximum_rotation,angles.length())
					for value in [angles.z,angles.x,angles.y]: values.append("%.7f"%value)
				file.store_line(" ".join(values))
			file.close()
			var slug = name.to_lower().replace("_","-")
			var title = name.replace("_Loop","").replace("_"," ")
			motions.append({"Id":"human-ual%d-%s"%[library,slug],"Name":title,"ActorFamily":"human","SourcePath":OUTPUT+filename,"SourceScale":1.0,"Duration":count/30.0,"Loop":looped,"Category":category(name),"Description":"Universal Animation Library %d · %s · authored animation"%[library,title],"Attribution":"Quaternius and Gonzalo Furnier · Universal Animation Library %d"%library,"License":"CC0-1.0","SourceUrl":"https://quaternius.itch.io/universal-animation-library"+("-2" if library==2 else ""),"Tags":["human","authored","quaternius","ual%d"%library,category(name).to_lower(),title.to_lower()]})
			audit.append({"library":library,"name":name,"frames":count,"fps":30,"source_seconds":animation.length,"maximum_rotation_degrees":maximum_rotation})
			print("UAL_CONVERTED %d %s %d frames"%[library,name,count])
		scene.queue_free()
		await process_frame
	FileAccess.open("res://assets/content/manifests/ual-motions.json",FileAccess.WRITE).store_string(JSON.stringify(motions,"  "))
	FileAccess.open(INPUT+"ual-conversion-audit.json",FileAccess.WRITE).store_string(JSON.stringify(audit,"  "))
	print("UAL_PREPARED clips="+str(motions.size()))
	quit(0)
