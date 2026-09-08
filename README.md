# RimTavern — RimWorld 剧情对话引擎

> RimWorld 1.6 · Galgame-style AI dialogue & story-event engine

RimTavern 把一个指定殖民者立为**故事主角**，其他角色作为**配角**，用 OpenAI 兼容的大模型驱动**剧情式对话**：NPC 开口后，为主角生成几个可选的回复（也可自由输入），并围绕"宏观叙事（殖民地命运/章节） ↔ 微观叙事（主角动机与抉择）"两条线组织长线剧情。

> 主角 + 配角 · 角色卡 · 世界书 · 场景感知 · 跨会话记忆/好感 · 可定义剧情事件链
RimTavern 在Mod中集成了简化的类似SillyTavern世界书+角色卡+事件+章节的剧情叙事，无需安装SillyTavern或其世界书/角色卡，本Mod有自己的简化格式。您可以任意编辑您的故事到对应路径下的.json文件中（见下文），我们也推荐您借助任何AI工具来将您的故事转变成结构化的世界书/角色卡/事件卡。如果您已有SillyTavern的世界书/角色卡，我们也支持导入。


> 需要玩家自备 LLM 接口（OpenAI / DeepSeek / Ollama / vLLM / LM Studio 等，任意 OpenAI 兼容 `/chat/completions` 端点）。

---
## 🍺 RimTavern 社区故事库
想要分享自己的故事？欢迎提交到https://github.com/WilliamLambert1205/RimTavern-Community-Cards

## ✨ 功能特性

- **galgame 对话流**：NPC 台词 → 主角 3~5 个选项 + 常驻自由输入框；可"换一批选项 / 重写对方上一句"（Swipe 式）、"发送后结束本段对话"。
- **主角与配角**：右键指定主角/常驻配角（随存档）；右键任意角色即可开聊；NPC 也会主动找主角。
- **角色卡**：作者层（性格要点 / 说话风格示范 / 背景）+ 运行时自动层（心情/职位/状态）混合；兼容 SillyTavern `chara_card_v2` JSON 导入；留空即用自动生成（无需任何内容也能玩）。
- **世界书（lorebook）**：常开与关键词条目（中英文别名均可命中）、预算折叠、按场景/对话自动激活；兼容 SillyTavern world-info 导入。
- **场景感知**：把"周围有哪些人/物/房间/装备/派系"确定性注入上下文，且只准引用清单内实体（不依赖 mod 名单、不编造世界）。
- **记忆与好感**：对话结束后台生成摘要 → 好感 ± / 剧情 flag；再开新对话自动"想起"上回（结构化记忆随存档）。
- **剧情事件系统（P5）**：JSON 编写事件（interval / afterDialogue / flagSet / affinity 触发），支持 **事件链**（完成解锁下一段）与开场注；内置事件编辑器。
- **历史与悬浮窗**：最近对话半透明悬浮条（可拖动/缩放/调字号）+ "全部历史"滚动窗口；历史随存档保存。
- **调试友好**：设置页一键复制"上传了什么 / 模型回了什么"的完整日志。

## 📦 安装与依赖

1. 依赖 [Harmony (brrainz.harmony)](https://github.com/pardeike/HarmonyRimWorld/releases/latest)。
2. 把 `RimTavern` 文件夹放入 RimWorld `Mods/`（或创意工坊订阅）。
3. 游戏内 Mod 设置 → **RimTavern** 填写 LLM 接口：
   - `Base URL`：OpenAI 兼容端点，如 `https://api.deepseek.com/v1`、本地 `http://127.0.0.1:11434/v1`（Ollama）等；
   - `API Key` / `Model`（如 `deepseek-chat`、`llama3`…）；
   - 点"测试连接"确认后即可游玩。

## 🎮 快速上手

1. 选中一名自由殖民者 → 右键其自身 → **设为故事主角**（右键他人可设为常驻配角）。
2. 选中主角 → 右键任意同地图人形角色 → **与 XX 交谈**。
3. 对话结束自动写历史并结算好感/记忆；之后 NPC 会在剧情事件到时**主动来找你**。

## 📄 作者内容（在这里书写自己的剧情）

| 目录 | 用途 | 格式 |
|---|---|---|
| `Cards/*.chara.json` | 角色卡（主角/配角人设覆盖） | chara v2 子集（name/description/scenario/mes_example） |
| `WorldBooks/*.worldinfo.json` | 世界书条目（常开 / 关键词） | ST world-info 子集：`entries[{uid,name,constant,keys[],content}]` |
| `Events/*.json` | 剧情事件（触发/链/flag/loreKeys） | 见下方示例与 `DEVELOPMENT_NOTES.md` |

Cards标准格式：
```json
{ "spec":"chara_card_v2","spec_version":"2.0",
  "data":{ "name":"…", "description":"性格要点…", "personality":"",
           "scenario":"背景/秘密(可选)", "first_mes":"", "mes_example":"说话风格示范 2~3 句" } }
```
WorldBooks标准格式：
```json
{ "name":"…",
  "entries":[{ "uid":"…","name":"…","constant":true,"enabled":true,
               "keys":["信标","beacon"],"comment":"…","content":"正文(≈≤300字)" }] }
```
Events标准格式：
```json
{ "id":"…","title":"…","opening":"开场注…","npc":"cast|colonist|visitor|any",
  "trigger":"interval|afterDialogue|flagSet|affinity",
  "intervalMinHours":8,"intervalMaxHours":24,"afterDialogueWindowHours":24,
  "afterFlag":"…","affinityMin":-101,"affinityMax":101,
  "cooldownHours":24,"initial":true,"maxTimes":99,
  "nextIds":[…],"setFlagsOnDone":[…],"loreKeys":[…] }
```
游戏内编辑写入 `_user_*.json`；**导入文件只读**（编辑即"复制为我的条目"，不改原文件）。
更完整的字段说明与事件示例见仓库内 [`DEVELOPMENT_NOTES.md`](DEVELOPMENT_NOTES.md)（末尾附三套格式规格）。

### 用提示词生成您的故事组件（示例）
这部分内容是很灵活的

玩家输入示例：
```
“这个星球原本被一个叫‘星联’的腐朽帝国统治，百年前崩溃了。现在北方是崇尚机械飞升的‘钢之民’，南方是崇拜自然灵能的‘绿嗣’。我的主角是一对从帝国冷冻仓逃出来的双胞胎姐妹，姐姐体质弱但有超凡灵能，妹妹是近战大师。她们想在中间地带建立第三势力。”

专用提示词（复制给 AI）：

请将上述史诗背景拆解为完整的叙事结构：
生成 2 个派系背景世界书：一个关于“钢之民”的机械化戒律，一个关于“绿嗣”的灵能共鸣仪式。
生成 双胞胎姐妹的角色卡：姐姐侧重 psycasts（灵能）背景，妹妹侧重格斗与机械师背景。性格一个柔软一个刚烈。
生成 3 个连锁事件链（nextIds 关联）：
事件 1（触发：interval 8h）：两派信使同时来访拉拢主角。
事件 2（触发：flagSet，完成选边站后）：敌对派系发动小规模骚扰袭击。
事件 3（触发：affinity 好感低于 -20 或高于 30）：秘密任务，寻找失落的帝国轨道卫星。
```


## 🐛 排障

- 对话没反应/报错：Mod 设置 → 调试日志区 → **复制调试日志** 后，把内容与 `Player.log` 一并贴到 Issue。
- 游戏运行中更新 DLL 会失败：请先关闭 RimWorld 再编译/部署。
- 内容未生效：确认文件在 `Cards/ WorldBooks/ Events/` 下，并在设置页点"重新加载内容文件"。

## 🗺 路线（节选）

- 承诺调度（对话中许下约定 → 数日后 NPC 依约而来）
- 世界书自动别名（翻译文件对齐）、可编辑上下文模板、群聊
- 详见 `DEVELOPMENT_NOTES.md`

## ⚖️ License

MIT License（详见仓库 LICENSE）。素材/角色卡示例仅供开发演示。
