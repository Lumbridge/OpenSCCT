extends SceneTree
## Compare gameplay bone transforms across a Blender round trip, by bone name.

func load_scene(file: String) -> Node3D:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	assert(doc.append_from_file(file, state) == OK)
	var scene := doc.generate_scene(state)
	root.add_child(scene)
	return scene

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var original := load_scene(args[0])
	var exported := load_scene(args[1])
	var a: Skeleton3D = original.find_children("*", "Skeleton3D", true, false)[0]
	var b: Skeleton3D = exported.find_children("*", "Skeleton3D", true, false)[0]
	var pa: AnimationPlayer = original.find_children("*", "AnimationPlayer", true, false)[0]
	var pb: AnimationPlayer = exported.find_children("*", "AnimationPlayer", true, false)[0]
	var max_position := 0.0
	var max_angle := 0.0
	var max_duration := 0.0
	var worst := ""
	var tested := 0
	var missing: Array = []
	for name in pa.get_animation_list():
		if not pb.has_animation(name):
			missing.append(name)
			continue
		var ca := pa.get_animation(name)
		var cb := pb.get_animation(name)
		max_duration = max(max_duration, abs(ca.length - cb.length))
		pa.play(name)
		pb.play(name)
		for phase in [0.0, 0.25, 0.5, 0.75, 1.0]:
			pa.seek(ca.length * phase, true)
			pb.seek(ca.length * phase, true)
			pa.advance(0)
			pb.advance(0)
			for i in a.get_bone_count():
				var j := b.find_bone(a.get_bone_name(i))
				if j < 0:
					missing.append(a.get_bone_name(i))
					continue
				var ta := a.global_transform * a.get_bone_global_pose(i)
				var tb := b.global_transform * b.get_bone_global_pose(j)
				var distance := ta.origin.distance_to(tb.origin)
				if distance > max_position:
					max_position = distance
					worst = "%s / %s / %s" % [name, a.get_bone_name(i), phase]
				max_angle = max(max_angle, ta.basis.orthonormalized().get_rotation_quaternion().angle_to(tb.basis.orthonormalized().get_rotation_quaternion()))
			tested += 1
	var passed := missing.is_empty() and max_position < 0.005 and max_angle < 0.03 and max_duration < 0.04
	var report := {"passed": passed, "sampled_poses": tested, "max_position_m": max_position,
		"max_angle_radians": max_angle, "max_duration_seconds": max_duration, "worst": worst, "missing": missing}
	print(JSON.stringify(report))
	if args.size() > 2:
		var file := FileAccess.open(args[2], FileAccess.WRITE)
		file.store_string(JSON.stringify(report, "  "))
	original.free()
	exported.free()
	quit(0 if passed else 1)
