extends Control

@onready var button = $Button

var data: Dictionary
var requiredtime
var menuname

var format_string = "현재 [%s] 조리 중

남은 조리 블록 %s 블록"

func _ready():
	update_text()


func update_text():
	if button == null:
		return
	
	print(SceneContext.block_index)
	var endtime = data["installedKitchenEquipment"]["cookingEndBlockIndex"]
	var currenttime = SceneContext.block_index["blockQuery"]["blocks"][0]["index"]
	var timeleft = endtime - currenttime
	button.text = format_string % [menuname, timeleft]


func set_data(info: Dictionary):
	data = info
	menuname = data["installedKitchenEquipment"]["cookingFood"]["name"]
	time_info()
	
func time_info():
	var recipe = SceneContext.recipe["recipe"]
	
	for singlerecipe in recipe:
		var rcpname = singlerecipe["resultFood"]["name"]
		if(rcpname == menuname):
			requiredtime = singlerecipe["requiredBlockCount"]
	
	print(requiredtime)