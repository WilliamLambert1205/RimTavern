# RimTavern — 开发日志（当前版总览 · 用于后续开发）

> RimWorld 1.6（krafs.rimworld.ref 1.6.4519-beta / Lib.Harmony 2.3.6，dotnet 9.0.100）。
> 定位：游戏内**剧情对话引擎**（galgame 式回合 + 事件调度 + 存档级叙事状态），
> 面向“宏观叙事层(殖民地命运/章节) ↔ 微观叙事层(主角体验/抉择)”双层叙事。
> 日志是工作文档：保留决策、规格、坑、文件格式与路线；日常过程性内容已精简。
> 最近更新：2026-09-05（P5 事件引擎+编辑器完成，Swipe/半透明风格对话窗完成）

---

## 一、当前功能地图（全部已实现并编译）

| 系统 | 要点 |
|---|---|
| 对话回合 | NPC 回合(LLM) → 选项生成(LLM，JSON) → 主角**选项+常驻输入框** → 循环；错误可 Retry |
| Swipe/重生成 | AwaitChoice 状态提供 [换一批选项] 与 [重写对方上一句] |
| 半透明风格 | 对话窗 `doWindowBackground=false`+自绘 alpha 底；历史悬浮窗同风格（可拖/缩放/调字号） |
| 入口 | 主角设定与右键对话（FloatMenu 1.6）；配角名单(存档)；NPC 主动自动事件；结束即归档 |
| 场景感知 | 确定性 SceneSource：Actors/MapContents/Equipment/Container/World → 场景块（变化才注入） |
| 角色卡 | 作者层(性格/范例/背景)+运行时自动层(PawnText)；绑定=存档，回退=名字→自动；导入 chara v2 子集 |
| 世界书 | 常开+关键词两档；键=界面语言+别名；预算折叠+去重；事件 loreKeys 可预激活 |
| 章节 | 设置两栏 chapterTitle/chapterNote → 【当前章节】作者笔记式注入 |
| 记忆/好感 | 收尾后台 LLM 摘要→好感±/记忆(结构化,存档)；新对话【此前记忆】回查 |
| 历史 | 悬浮窗最近 N 条 + [全部历史]滚动窗（存档 60 场） |
| P5 事件 | JSON 定义、四类触发、NPC 条件、链(nextIds)、flag、调试强制触发、事件编辑器 |
| 调试 | Diag 环形日志(150)+复制；记录上传全量(req)/模型原文(resp)/注入/事件(mem/ev 系) |

## 二、交互/行为规格（玩家侧事实）

- 主角：右键其自身“设为故事主角”；配角：右键“设为/移出常驻配角”（均存档）。
- 对话：同地图右键即开；1 速锁定(可暂停)；选项 2–5(可调)；输入常驻；[发送后结束本段对话] 默认关；
  [换一批选项]/[重写对方上一句]；结束=Esc/X/结束对话→自动收尾摘要+好感+归档。
- 事件触发时 npcInitiated+【事件】标题+开场注(事件起因)；事件对话也走同一窗口。
- 悬浮窗：仅 MapComponent 自动回调绘制（事件最前段，勿放 UIRoot Postfix——事件已被消费；另一会话已验证）。窗口矩形跨 pass 状态保存，非空闲不重读设置。设置页可开关/行数/字号/重开按钮。

## 三、Prompt 注入顺序（契约）

`人设卡(现状+作者层) → 记忆(此前提) → 【当前章节】 → 【相关背景】(常开+命中+事件loreKeys) → 开场/继续 → 场景块(变化才注入) → 规则(1-6) → user 指令`
预算默认：场景 900 / Lore 500 / 记忆 400 字符（设置可调）。规则固定含：中文、1~3 句、禁括号动作/引号、语气贴卡、只提真实存在、**严禁逐字照抄说话范例/注引文本**、不复读。

## 四、模块与文件

- `RimTavernMod.cs` 入口+可滚动设置页（measure/draw 双遍）；`Settings.cs` 全部设置(ExposeData)。
- `Core/RimTavernGameComp.cs` 存档状态中枢：主角/配角/卡绑定/记忆/好感/对话归档/**storyFlags/事件实例/解锁/冷却/最后对话**；收尾摘要出队主线程应用；tick 驱动事件扫描(600)。
- `Interaction/TalkFloatMenuPatch.cs`；`Logic/{DialogueSession,ContextAssembler,LlmClient,PawnText}.cs`；
  `Perception/SceneBuilder.cs`；`Data/{ContentStore,LoreEntry,CharacterCard,DialogueArchive,StoryMemory,StoryEvent,EntityKey}.cs`；
  `Util/{Json,Diag}.cs`；`UI/{TavernDialogueWindow,TavernHistory,TavernHistoryOverlay,HistoryWindow,CharCardEditorWindow,CharCardAssignWindow,WorldbookEditorWindow,EventEditorWindow}.cs`

## 五、存档/文件存储（作者内容与用户文件语义）

- 作者内容目录：`Cards/*.chara.json`、`WorldBooks/*.worldinfo.json`、`Events/*.json`（启动扫描+设置重载）。
- 游戏内编辑写 `_user_*.json`；**文件条目只读**，编辑器“复制为我的条目/副本”不改原文件；即时写盘。
- 存档内（ExposeData）状态：主角/配角/绑定/记忆/好感/互动tick/对话归档/**事件 flags/解锁/次数/冷却/在途实例/最后对话**/章节栏为设置级。
- 自动层（BuildAuto 卡、场景）永不落盘 → 不会覆盖作者编辑。

## 六、P5 事件引擎（v1 规格定稿）

- 定义：`Events/*.json`（数组或单对象）。字段见下文格式节。
- 触发器：`interval`（随机窗口，可重复）/`afterDialogue`（刚与满足 npc 条件的对象聊完 N 小时内，链主要用它）/`flagSet`/`affinity`。
- NPC：`cast|colonist|visitor|any`；找不到时回退同地图任意人形（好感区间仍生效）；强制触发无视冷却/计时。
- 完成：对话正常结束 → 实例 done → 次数+1 → setFlagsOnDone → 解锁 nextIds。解锁规则：`initial:true` 或不被任何 nextIds 引用。
- 状态全部随存档；设置“管理事件定义（编辑器）”、调试“强制触发”。

## 七、P4-5 承诺调度器（机制定稿，未实现；手动承诺暂不做）

- 认定：主角主动、指向当前NPC、含“我+动词”承诺句式；收尾 LLM `promises[]`+引擎校验；同类只留一条。
- 到期：引擎随机 24–72 游戏小时（不信任模型时间）；到点 NPC 自动弹窗带承诺开场注；触发一次即完结；NPC 不可用顺延×3 后丢弃；手动管理需 RimChat 式实体/任务机制→暂缓。

## 八、调试日志段（复制日志定位：设置→调试日志区）

`req|npc-turn/options-gen`(上传全量) · `resp|…`(原文) · `parse|npc-line` · `scene|refresh` · `inject|scene` ·
`mem|outcome` · `ev|fire/done/scan` · `test|prompt/resp` · `error|*`
新增上下文来源时必须新增 section 并登记于此，并在注入前打印“命中/未命中原因”。

## 九、关键坑（迁移备忘）

- GameComponent 子类 `ctor(Game)` 不可 `: base(game)`；RimWorld 用反射 Activator 实例化。
- MapComponentOnGUI 引擎自动调用；悬浮窗拖动需“跨 pass 状态+事件/Input 混合”，勿每帧重读设置矩形；勿在 UIRoot Postfix 追加绘制（此路已弃）。
- 设置页/长列表必须自带滚动（Listing 不滚）。
- `Widgets.TextFieldLabeled` 不存在；用 Label+TextField 手排。
- `Json.Str` 是 `JsonNode.Str`；`def.passability` 比较用 `Traversability.Standable`；`GenDate.DateFullStringAt(tick, Vector2)` 需 tileCenter。
- 编译时若 `1.6\Assemblies\RimTavern.dll` 被游戏占用会失败：先关游戏再 build。
- 事件编辑器/世界书编辑器都需外层滚动，避免按钮被挤出。

## 十、格式标准（Cards / WorldBooks / Events）

详见文件底部“文件格式规格”。（本文件与 `TESTING.md`、`RimModRef` 参照一起构成开发资料。）

## 十一、路线与待办（下一步候选）

1. **规则7 防照抄说话范例**（日志观察到 NPC 偶尔整句引用卡里示范文本）。
2. 事件编辑器已含保存/删除/复制（文件只读）；补 `ev|scan` 周期无命中原因。
3. **P4-5 承诺调度自动版实现**（机制已定稿）。
4. 泛化世界书别名（untranslated→翻译表自动别名，P3-7）。
5. 借鉴 ST 候选：Swipe✅、消息编辑、世界书 position/depth/概率、可编辑 instruct/上下文模板、群聊、向量记忆(后期)。
6. 明确不做（防膨胀）：AI-AI 自由对话、日常闲聊气泡入口(M4)、RimChat 式实体任务(承诺手动版)先不做。

---

# 附：文件格式规格（Cards / WorldBooks / Events）

## Cards —— 角色卡（Cards/*.chara.json）

chara_card_v2 子集；引擎只读以下字段（其余忽略）：

```json
{
  "spec": "chara_card_v2",
  "spec_version": "2.0",
  "data": {
    "name": "角色名（用于名字匹配回退）",
    "description": "性格要点：固定人设、说话基调（ST 卡常把外观+人格都写这里；建议精简为中文）",
    "personality": "",
    "scenario": "背景/秘密/当前处境（可选）",
    "first_mes": "",
    "mes_example": "说话风格示范（2~3 句，模型模仿语气但不应照抄）"
  }
}
```

- 运行时 = 自动层（PawnText：名字/年龄/职位/特质/心情/忙碌）+ 作者层（上表三块）；作者层留空即用自动层。
- 绑定优先：存档绑定卡 → 名字匹配 → 自动。用户卡存 `Cards/_card_<名>.chara.json`（写盘即绑定）。

## WorldBooks —— 世界书（WorldBooks/*.worldinfo.json）

ST world_info 子集（导入兼容）；UI 参数(概率/深度/位置/selective logic 等)会被丢弃：

```json
{
  "name": "书名称",
  "entries": [
    {
      "uid": "唯一id",
      "name": "条目名(列表/日志显示)",
      "constant": true,
      "enabled": true,
      "keys": ["关键词1", "alias_en", "别名2"],
      "comment": "备注(可选)",
      "content": "条目正文（≤约300字符为佳，过长会被预算截断）"
    }
  ]
}
```

- `constant:true` 每轮全注入；否则任一 `keys` 命中“场景/最近对话/事件 loreKeys”即激活；子串匹配（大小写不敏感）。
- 中文键+英文别名都放 keys，可双向命中。游戏内编辑存 `_user.worldinfo.json`。

## Events —— 剧情事件（Events/*.json）

支持两种形态：单事件对象，或 `{"events":[ ... ]}` 数组。字段：

```json
{
  "id": "唯一id（不可重复；文件间重名以先加载为准）",
  "title": "显示名",
  "opening": "开场注（注入 NPC system 的“事件起因”，模型须围绕它）",
  "npc": "cast | colonist | visitor | any（找不到时回退任意人形）",
  "trigger": "interval | afterDialogue | flagSet | affinity",
  "intervalMinHours": 8,
  "intervalMaxHours": 24,
  "afterDialogueWindowHours": 24,
  "afterFlag": "flagSet 时等待的 flag 名",
  "affinityMin": -101,
  "affinityMax": 101,
  "cooldownHours": 24,
  "initial": true,
  "maxTimes": 99,
  "nextIds": ["链上下一事件id"],
  "setFlagsOnDone": ["完成后置为 true 的 flag"],
  "loreKeys": ["开场强制命中的世界书关键词"]
}
```

- 触发条件：interval=随机窗口到点(可重复)；afterDialogue=刚与满足 npc 条件的对象对话结束 N 小时内(链靠它)；flagSet=flag 为真；affinity=该 NPC 好感在区间。
- 完成=对话正常结束；完成置 flags、次数+1、解锁 nextIds。
- 调试：设置页“强制触发下一个可用事件(无视冷却)” + 事件编辑器（增删改写 `_user_events.json`，文件条目只读）。
