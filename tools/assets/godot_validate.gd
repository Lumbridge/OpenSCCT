extends SceneTree
## Standalone runtime glTF import smoke. Run with --headless --script and -- GLBs.

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var failed := false
	for file in OS.get_cmdline_user_args():
		var state := GLTFState.new()
		var document := GLTFDocument.new()
		var error := document.append_from_file(file, state)
		if error != OK:
			push_error("GLB import failed: %s (%s)" % [file, error])
			failed = true
			continue
		var scene := document.generate_scene(state)
		if scene == null:
			failed = true
			continue
		root.add_child(scene)
		var meshes := scene.find_children("*", "MeshInstance3D", true, false)
		var skeletons := scene.find_children("*", "Skeleton3D", true, false)
		var clips: Dictionary = {}
		var motion: Dictionary = {}
		for player: AnimationPlayer in scene.find_children("*", "AnimationPlayer", true, false):
			for clip_name in player.get_animation_list():
				var clip := player.get_animation(clip_name)
				clips[clip_name] = clip.length
				player.play(clip_name)
				player.seek(0, true)
				player.advance(0)
				var before: Array = []
				for skeleton: Skeleton3D in skeletons:
					for i in skeleton.get_bone_count():
						before.append(skeleton.get_bone_global_pose(i).origin)
				player.seek(clip.length / 2.0, true)
				player.advance(0)
				var delta := 0.0
				var index := 0
				for skeleton: Skeleton3D in skeletons:
					for i in skeleton.get_bone_count():
						delta = max(delta, before[index].distance_to(skeleton.get_bone_global_pose(i).origin))
						index += 1
				motion[clip_name] = delta
		var bones := 0
		for skeleton: Skeleton3D in skeletons:
			bones += skeleton.get_bone_count()
			for i in skeleton.get_bone_count():
				if not skeleton.get_bone_global_pose(i).is_finite():
					failed = true
		if meshes.is_empty():
			failed = true
		print(JSON.stringify({"file": file, "meshes": meshes.size(), "bones": bones, "clips": clips, "bone_motion_m": motion}))
		scene.free()
	quit(1 if failed else 0)
