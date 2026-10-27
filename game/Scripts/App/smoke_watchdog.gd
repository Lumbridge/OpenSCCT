extends Node
## Safety net for headless smoke runs (-- --svm-smoke=name).
##
## The smoke runner itself is C#. If the game assembly is missing or fails to load, no C# code runs
## and the process would never quit, which hangs CI. This script does not depend on .NET: it fails
## the run when the C# runner has not appeared after a while, and enforces a hard time limit.

const SMOKE_PREFIX := "--svm-smoke="
const TIMEOUT_PREFIX := "--svm-smoke-timeout="
## Keep in step with LaunchOptions.DefaultSmokeTimeoutSeconds.
const DEFAULT_TIMEOUT_SEC := 120.0
## Extra time on top of the C# timeout, so the C# runner normally reports first.
const HARD_LIMIT_MARGIN_SEC := 30.0
const RUNNER_START_LIMIT_SEC := 15.0
const RUNNER_PATH := "/root/App/SmokeRunner"
const EXIT_RUNNER_MISSING := 3
const EXIT_HARD_TIMEOUT := 4

var _name := ""
var _hard_limit_sec := DEFAULT_TIMEOUT_SEC + HARD_LIMIT_MARGIN_SEC
var _start_msec := 0
var _runner_seen := false


func _ready() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with(SMOKE_PREFIX):
			_name = arg.substr(SMOKE_PREFIX.length())
		elif arg.begins_with(TIMEOUT_PREFIX):
			var seconds := arg.substr(TIMEOUT_PREFIX.length()).to_float()
			if seconds > 0.0:
				_hard_limit_sec = seconds + HARD_LIMIT_MARGIN_SEC
	if _name.is_empty():
		set_process(false)
		return
	process_mode = Node.PROCESS_MODE_ALWAYS
	_start_msec = Time.get_ticks_msec()


func _process(_delta: float) -> void:
	var elapsed := (Time.get_ticks_msec() - _start_msec) / 1000.0
	if not _runner_seen:
		_runner_seen = get_node_or_null(RUNNER_PATH) != null
		if not _runner_seen and elapsed > RUNNER_START_LIMIT_SEC:
			_fail(EXIT_RUNNER_MISSING, "C# smoke runner did not start (is the game assembly built?)")
			return
	if elapsed > _hard_limit_sec:
		_fail(EXIT_HARD_TIMEOUT, "hard time limit of %.0f s reached" % _hard_limit_sec)


func _fail(exit_code: int, reason: String) -> void:
	set_process(false)
	printerr("SVM_SMOKE %s FAIL %s" % [_name, reason])
	get_tree().quit(exit_code)
