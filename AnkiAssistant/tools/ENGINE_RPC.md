# 内置 Anki 引擎（`rslib_aa.dll`）RPC 表与字段号依据

本文是 Windows 版 AnkiAssistant 调用内部 Anki 引擎（官方 Rust `rslib`）的**唯一权威对照表**。
`src\Engine.cs`、`src\EngineRpc.cs` 里的每个 service/method 编号与字段号都能在这里查到出处。

---

## 0. 编号的权威来源（**不要从 proto 的书写顺序推编号**）

**编号不在 `.proto` 里。** 唯一权威是引擎自己生成的 Python 绑定，每个 `def xxx_raw` 的方法体
就是一次 `self._run_command(service, method, message)`，编号是显式数字：

```
D:\Anki-Android-Backend\anki\out\pylib\anki\_backend_generated.py
  109683 字节 / 2310 行 / mtime 2026-10-05 10:22:16
```

克隆 commit（要复核就 checkout 这两个）：

| 仓库 | commit |
| --- | --- |
| `D:\Anki-Android-Backend`（外层） | `39e72a117ee1e3f165bff4e15b1a8bcc531f1d3d` |
| `D:\Anki-Android-Backend\anki`（子模块） | `29bb700b951e3f0c0cb69b77c0180fc1fe33e6ba` |

**三条独立路径已互相印证**，编号可以放心用：

1. `_backend_generated.py` 里的 `_run_command(s, m, …)` 数字（下表 `line=` 列）。
2. `rsdroid\build\generated\source\backend\GeneratedBackend.kt`
   （例如 `:123-124` `openCollectionRaw` → `runMethodRaw(3, 0, input)`；`:393-394` `addDeckRaw` → `runMethodRaw(7, 1, input)`）。
3. DLL 那一路用 `svc_table` 直接从 `descriptors.bin` dump，实测 `BackendCollectionService=3 / open_collection=0`、
   `BackendDecksService=7 / get_deck_names=13`。

消息结构（字段号）的权威来源是 `.proto`：

```
D:\Anki-Android-Backend\anki\proto\anki\{generic,collection,decks,notes,notetypes,search,config,media,sync}.proto
```

> ⚠️ `backend.proto` 的方法**声明顺序不可靠**：`get_services()` 会把 delegating 方法排在 trait 方法之后。
> 所以只有 `_backend_generated.py` 的数字算数，proto 只用来查消息字段号。

---

## 1. C ABI（`src\Engine.cs` 的 DllImport，照抄不改）

```c
int32_t aa_open(const uint8_t* data, size_t data_len, uint64_t* out_handle, uint8_t** out_buf, size_t* out_len);
int32_t aa_call(uint64_t handle, int32_t service, int32_t method, const uint8_t* data, size_t data_len, uint8_t** out_buf, size_t* out_len);
void    aa_close(uint64_t handle);
void    aa_free(uint8_t* buf, size_t len);
const char* aa_version(void);
const char* aa_last_error(void);
```

- `0` = 成功；非 0 = 失败，此时 `out_buf` 里是 **UTF-8 的错误文本**（同样要 `aa_free`）。
  `Engine.ReadNativeError` 优先读 `aa_last_error()`，读不到再按 `BackendError{string message = 1}` 解 `out_buf`。
- 缓冲区由 Rust 分配，**必须** `aa_free` 释放，不能 `Marshal.FreeHGlobal`。
- `size_t` 在 x64 = 8 字节；C# 侧统一用 `UIntPtr`（`Engine.TakeBuffer(IntPtr, UIntPtr)`）。
- DLL 是**静态 CRT**（`dumpbin /dependents` 里没有 `vcruntime140/msvcp140/ucrtbase`），
  分发时不需要附带任何 VC 运行库。
- 实测版本串：`aa-ffi 0.1.0 / rslib anki 26.09.3 (build ) / windows-x86_64`。

### ⚠️ 并发：同一个 handle 绝对不能并发调用

`aa_call` 内部**没有加锁**，实现就是 `&mut *(handle as *mut Backend)`（跟 rsdroid 的 JNI 桥一个写法），
同一 handle 被多线程同时调用的行为**从未验证过**。

`Engine.Call` / `Engine.Open` / `Engine.Close` 已经用一把进程级锁 `Engine._callLock`
（`static readonly object _callLock = new object();`）把所有原生调用串行化。
**新增任何直接走 DllImport 的代码，都必须走这三个入口**，不要绕过锁。
（用单独的 `_callLock` 而不是 `_lock`：一次同步 RPC 可能跑几分钟，不能顺手把 `EnsureLoaded` 堵死。）

---

## 2. 消息结构总表（字段号 + proto 出处）

### `generic.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `Empty` | （空） | :10 |
| `Int32` | `sint32 val = 1` | :12-14 |
| `UInt32` | `uint32 val = 1` | :16-18 |
| `Int64` | `int64 val = 1` | :20-22 |
| `String` | `string val = 1` ← **字段名是 `val` 不是 `str`** | :24-26 |
| `Json` | `bytes json = 1` | :28-30 |
| `Bool` | `bool val = 1` | :32-34 |
| `StringList` | `repeated string vals = 1` | :36-38 |

### `collection.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `OpenCollectionRequest` | `collection_path=1, media_folder_path=2, media_db_path=3` | :43-47 |
| `CloseCollectionRequest` | `downgrade_to_schema11=1` | :49-51 |
| `OpChanges` | `card=1, note=2, deck=3, tag=4, notetype=5, config=6, browser_table=7, browser_sidebar=8, note_text=9, study_queues=10, deck_config=11, mtime=12`（全 bool） | :57-73 |
| `OpChangesWithCount` | `changes=1, uint32 count=2` | :85-88 |
| `OpChangesWithId` | `changes=1, int64 id=2` | :90-93 |

### `decks.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `DeckId` | `int64 did=1` | :46-48 |
| `DeckIds` | `repeated int64 dids=1` | :50-52 |
| `Deck.Common` | `study_collapsed=1, browser_collapsed=2, last_day_studied=3, new_studied=4, review_studied=5, learning_studied=6, milliseconds_studied=7` | :55-71 |
| `Deck.Normal` | `config_id=1, extend_new=2, extend_review=3, description=4, markdown_description=5, optional review_limit=6, optional new_limit=7` | :72-90 |
| `Deck.Filtered` | `…` | :91-124 |
| `Deck` | `id=1, name=2, mtime_secs=3, usn=4, Common common=5, oneof kind { Normal normal=6; Filtered filtered=7; }` | :134-144 |
| `GetDeckNamesRequest` | `skip_empty_default=1, include_filtered=2` | :196-200 |
| `DeckNames` | `repeated DeckNameId entries=1` | :202-204 |
| `DeckNameId` | `int64 id=1, string name=2` | :206-209 |

### `notes.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `NoteId` | `int64 nid=1` | :38-40 |
| `NoteIds` | `repeated int64 note_ids=1` | :42-44 |
| `Note` | `id=1, guid=2, notetype_id=3, mtime_secs=4, usn=5, repeated string tags=6, repeated string fields=7` | :46-54 |
| `AddNoteRequest` | `Note note=1; int64 deck_id=2` | :56-59 |
| `AddNoteResponse` | `OpChangesWithCount changes=1; int64 note_id=2` | :61-64 |
| `RemoveNotesRequest` | `repeated int64 note_ids=1; repeated int64 card_ids=2` | :89-92 |
| `FieldNamesForNotesRequest` | `repeated int64 nids=1` | :104-106 |
| `FieldNamesForNotesResponse` | `repeated string fields=1` | :108-110 |

### `notetypes.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `NotetypeId` | `int64 ntid=1` | :43-45 |
| `NotetypeNames` | `repeated NotetypeNameId entries=1` | :170-172 |
| `NotetypeNameId` | `int64 id=1, string name=2` | :178-181 |

### `search.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `SearchRequest` | `string search=1; SortOrder order=2` | :113-116 |
| `SearchResponse` | `repeated int64 ids=1` | :118-120 |
| `SortOrder` | `oneof value { Empty none=1; string custom=2; Builtin builtin=3; }` | :122-132 |

`rslib\src\search\service\mod.rs:35` 与 `:46`：`let order = input.order.unwrap_or_default().value.into();`
⇒ **`order` 可以完全不设**（得到默认排序），所以只写 `search=1` 是安全的。

### `config.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `SetConfigJsonRequest` | `string key=1; bytes value_json=2; bool undoable=3` | :99-103 |

`GetConfigJson` 请求是 `generic.String{val=1}`，响应是 `generic.Json{json=1}`（里面是 JSON 文本的字节）。

### `media.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `AddMediaFileRequest` | `string desired_name=1; bytes data=2` | :44-47 |

响应是 `generic.String{val=1}`，**可能是改写过的文件名**（重名会加后缀），要用返回值而不是入参。

### `sync.proto`

| 消息 | 字段 | 行号 |
| --- | --- | --- |
| `SyncAuth` | `string hkey=1; optional string endpoint=2; optional uint32 io_timeout_secs=3` | :29-33 |
| `SyncLoginRequest` | `string username=1; string password=2; optional string endpoint=3` | :35-39 |
| `SyncStatusResponse` | `Required required=1; optional string new_endpoint=4` | :41-49 |
| `SyncCollectionRequest` | `SyncAuth auth=1; bool sync_media=2` | :51-54 |
| `SyncCollectionResponse` | `host_number=1, server_message=2, required=3, new_endpoint=4, server_media_usn=5` | :56-72 |
| `FullUploadOrDownloadRequest` | `SyncAuth auth=1; bool upload=2; optional int32 server_usn=3` | :85-90 |

枚举：`SyncStatusResponse.Required` = `NO_CHANGES=0, NORMAL_SYNC=1, FULL_SYNC=2`；
`SyncCollectionResponse.ChangesRequired` 另有 `FULL_DOWNLOAD=3, FULL_UPLOAD=4`。

---

## 3. RPC 表（动作 → service/method）

`line=` 是 `_backend_generated.py` 里对应 `def xxx_raw` 的行号。

### `BackendSyncService` = 1

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| `sync_media` | 1/0 | SyncMediaRequest | — | `line=45` |
| `abort_media_sync` | 1/1 | Empty | Empty | `line=55` |
| `media_sync_status` | 1/2 | Empty | MediaSyncStatusResponse | `line=74` |
| **`sync_login`** | **1/3** | SyncLoginRequest | SyncAuth | `line=79`；proto `sync.proto:21` |
| **`sync_status`** | **1/4** | SyncAuth | SyncStatusResponse | `line=89`；proto `sync.proto:22` |
| **`sync_collection`** | **1/5** | SyncCollectionRequest | SyncCollectionResponse | `line=99`；proto `sync.proto:23` |
| **`full_upload_or_download`** | **1/6** | FullUploadOrDownloadRequest | Empty | `line=109`；proto `sync.proto:24` |
| `abort_sync` | 1/7 | Empty | Empty | `line=119` |
| `set_custom_certificate` | 1/8 | — | — | `line=129` |

### `BackendCollectionService` = 3

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| **`open_collection`** | **3/0** | OpenCollectionRequest | Empty | `line=139`；proto `collection.proto:29` |
| **`close_collection`** | **3/1** | CloseCollectionRequest | Empty | `line=149`；proto `collection.proto:30` |
| `latest_progress` | 3/4 | Empty | Progress | `line=191` |
| `set_wants_abort` | 3/5 | — | — | — |
| `check_database` | 3/6 | CheckDatabaseRequest | OpChangesWithCount | — |
| `get_undo_status` | 3/7 | Empty | UndoStatus | — |
| `undo` / `redo` | 3/8 / 3/9 | Empty | OpChanges | — |
| `add_custom_undo_entry` | 3/10 | — | — | — |
| `merge_undo_entries` | 3/11 | — | — | — |
| `set_load_balancer_enabled` | 3/14 | — | — | — |
| `get_custom_colours` | 3/15 | — | — | — |

### `BackendDecksService` = 7

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| `new_deck` | 7/0 | Empty | Deck | `line=341` |
| **`add_deck`** | **7/1** | **`Deck`（没有包装！）** | OpChangesWithId | `line=351`；proto `decks.proto:15` |
| `add_deck_legacy` | 7/2 | Json | OpChangesWithId | `line=361` |
| `add_or_update_deck_legacy` | 7/3 | Json | OpChangesWithId | — |
| `deck_tree` | 7/4 | — | — | — |
| `deck_tree_legacy` | 7/5 | — | — | — |
| `get_all_decks_legacy` | 7/6 | — | Json | — |
| **`get_deck_id_by_name`** | **7/7** | `generic.String` | `DeckId` | `line=411`；proto `decks.proto:21` |
| `get_deck` | 7/8 | DeckId | Deck | — |
| `update_deck` | 7/9 | Deck | OpChanges | — |
| `update_deck_legacy` | 7/10 | Json | OpChanges | — |
| `set_deck_collapsed` | 7/11 | — | — | — |
| `get_deck_legacy` | 7/12 | — | — | — |
| **`get_deck_names`** | **7/13** | GetDeckNamesRequest | DeckNames | `line=471`；proto `decks.proto:27` |
| `get_deck_and_child_names` | 7/14 | — | — | — |
| `new_deck_legacy` | 7/15 | — | — | — |
| `remove_decks` | 7/16 | DeckIds | OpChangesWithCount | `line=501` |
| `reparent_decks` | 7/17 | — | — | — |
| `rename_deck` | 7/18 | — | — | — |
| `get_or_create_filtered_deck` | 7/19 | — | — | — |
| `set_current_deck` / `get_current_deck` | 7/22 / 7/23 | — | — | — |

> `add_deck` 的请求**就是 `Deck` 消息本身**，不是 `{deck: Deck}` 之类的包装。
> `EngineRpc.AddDeck` 写的是 `Deck{ name=2; common=5; oneof kind{ normal=6 } }`。

### `BackendConfigService` = 9

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| **`get_config_json`** | **9/0** | `generic.String{val=1}` | `generic.Json{json=1}` | `line=581`；proto `config.proto:14` |
| **`set_config_json`** | **9/1** | SetConfigJsonRequest | OpChanges | `line=591`；proto `config.proto:15` |
| `set_config_json_no_undo` | 9/2 | — | — | — |
| `remove_config` | 9/3 | — | — | — |
| `get_all_config` | 9/4 | — | — | — |
| `get_config_bool` / `set_config_bool` | 9/5 / 9/6 | — | — | — |
| `get_config_string` / `set_config_string` | 9/7 / 9/8 | — | — | — |
| `get_preferences` / `set_preferences` | 9/9 / 9/10 | — | — | — |

### `BackendNotetypesService` = 23

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| `add_notetype` | 23/0 | Notetype | OpChangesWithId | `line=1245` |
| `update_notetype` | 23/1 | — | — | — |
| **`add_notetype_legacy`** | **23/2** | `generic.Json{json=1}` | OpChangesWithId | `line=1265`；proto `notetypes.proto:16` |
| `update_notetype_legacy` | 23/3 | — | — | — |
| `add_or_update_notetype` | 23/4 | — | — | — |
| `get_stock_notetype_legacy` | 23/5 | — | — | — |
| **`get_notetype`** | **23/6** | NotetypeId | Notetype | `line=1305` |
| `get_notetype_legacy` | 23/7 | — | — | — |
| **`get_notetype_names`** | **23/8** | Empty | NotetypeNames | `line=1325`；proto `notetypes.proto:23` |
| `get_notetype_names_and_counts` | 23/9 | — | — | — |
| **`get_notetype_id_by_name`** | **23/10** | `generic.String` | NotetypeId | `line=1345`；proto `notetypes.proto:25` |
| `remove_notetype` | 23/11 | — | — | — |
| `get_aux_notetype_config_key` | 23/12 | — | — | — |
| `get_aux_template_config_key` | 23/13 | — | — | — |
| `get_change_notetype_info` | 23/14 | — | — | — |
| `change_notetype` | 23/15 | — | — | — |
| **`get_field_names`** | **23/16** | `NotetypeId{ntid=1}` | `generic.StringList{vals=1}` | `line=1405`；proto `notetypes.proto:33` |
| `restore_notetype_to_stock` | 23/17 | — | — | — |
| `get_cloze_field_ords` | 23/18 | — | — | — |

### `BackendNotesService` = 25

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| `new_note` | 25/0 | NotetypeId | Note | `line=1435` |
| **`add_note`** | **25/1** | AddNoteRequest | AddNoteResponse | `line=1445`；proto `notes.proto:17` |
| `add_notes` | 25/2 | AddNotesRequest | OpChangesWithCount | `line=1455` |
| `defaults_for_adding` | 25/3 | — | — | — |
| `default_deck_for_notetype` | 25/4 | — | — | — |
| `update_notes` | 25/5 | — | — | — |
| **`get_note`** | **25/6** | `NoteId{nid=1}` | `Note` | `line=1495`；proto `notes.proto:22` |
| **`remove_notes`** | **25/7** | RemoveNotesRequest | OpChangesWithCount | `line=1505`；proto `notes.proto:23` |
| `cloze_numbers_in_note` | 25/8 | — | — | — |
| `after_note_updates` | 25/9 | — | — | — |
| `field_names_for_notes` | 25/10 | FieldNamesForNotesRequest | FieldNamesForNotesResponse | `line=1535`；proto `notes.proto:27` |
| `note_fields_check` | 25/11 | — | — | — |
| `cards_of_note` | 25/12 | — | — | `line=1555` |
| `get_single_notetype_of_notes` | 25/13 | — | — | — |

`_backend_generated.py:1448` 的签名 `add_note(*, note: anki.notes_pb2.Note, deck_id: int)`
印证了 **`deck_id` 是 `AddNoteRequest` 里的独立字段**。

### `BackendSearchService` = 29

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| `build_search_string` | 29/0 | SearchNode | — | `line=1735` |
| `search_cards` | 29/1 | SearchRequest | SearchResponse | `line=1745` |
| **`search_notes`** | **29/2** | `SearchRequest{search=1}`（`order` 可省） | `SearchResponse{ids=1}` | `line=1755`；proto `search.proto:16` |
| `join_search_nodes` | 29/3 | — | — | — |
| `replace_search_node` | 29/4 | — | — | — |
| `find_and_replace` | 29/5 | — | — | — |
| `all_browser_columns` | 29/6 | — | — | — |
| `browser_row_for_id` | 29/7 | — | — | — |
| `set_active_browser_columns` | 29/8 | — | — | — |

### `BackendMediaService` = 41

| 动作 | svc/method | 请求 | 响应 | 出处 |
| --- | --- | --- | --- | --- |
| `add_media_from_url` | 41/0 | — | — | `line=2051` |
| `check_media` | 41/1 | Empty | CheckMediaResponse | `line=2061` |
| **`add_media_file`** | **41/2** | AddMediaFileRequest | `generic.String`（实际文件名） | `line=2071`；proto `media.proto:15` |
| `add_media_from_path` | 41/3 | — | — | — |
| `trash_media_files` | 41/4 | — | — | — |
| `empty_trash` / `restore_trash` | 41/5 / 41/6 | — | — | — |
| `extract_static_media_files` | 41/7 | — | — | — |
| `extract_media_files` | 41/8 | — | — | — |
| `get_absolute_media_path` | 41/9 | — | — | — |

---

## 4. 卡片落牌组：靠 `AddNoteRequest.deck_id`

原生引擎里 **不存在** AnkiConnect 那种「先把卡加到默认牌组、再用 `changeDeck` 搬过去」的需求。
`AddNoteRequest`（`notes.proto:56-59`）自己就带 `int64 deck_id = 2`：

```
AddNoteRequest { Note note = 1; int64 deck_id = 2; }
```

`EngineRpc.AddNote(deckName, modelName, fields, tags)` 的顺序是：

1. `DeckIdByName(deckName)`（7/7 `get_deck_id_by_name`，找不到返回 -1）；
2. 返回 -1 就 `AddDeck(deckName)`（7/1）再查一次；
3. `NotetypeIdByName(modelName)`（23/10）；
4. 组 `Note{ notetype_id=3; repeated string tags=6; repeated string fields=7 }`
   放进 `AddNoteRequest{ note=1, deck_id=2 }` 调 25/1；
5. 从 `AddNoteResponse.note_id`（字段 2）取新 id。

不需要任何事后搬牌组。`Note.id` 留 0（让引擎分配），`usn` 也不设。

---

## 5. 已知的坑（都是实测踩出来的）

### 5.1 `field_names_for_notes` 的**顺序跟 `Note.fields` 对不上**

`EngineRpc.FieldNamesForNotes(ids)`（25/10）对字段 `Front/Back` 的笔记返回 **`[Back, Front]`**
（看着像按名字排序），而同一个 note 的 `Note.fields` 是 `[Front 的值, Back 的值]`。
**按下标配对会张冠李戴。**

要按下标取字段值，必须用 `EngineRpc.FieldNames(notetypeId)`（23/16 `get_field_names`），
它按 notetype 里的 `ord` 顺序返回 `[Front, Back]`。

`EngineRpc.NotesInfo(ids)` 已经按这个结论实现：按 `notetype_id` 缓存一次 `FieldNames`，
再赋给 `AaNoteInfo.FieldNames`，这样 `Field("Back")` 拿到的才是对的。
证据在 `tools\engine_probe.cs` 的第 8 步（会把两个列表都打出来）。

### 5.2 `add_notetype_legacy` 的 legacy JSON 形状

`EngineRpc.BuildNotetypeJson` 生成的就是能被 23/2 接受的形状：

- `id` 必须是 **0**；
- `flds` / `tmpls` 是**对象数组**（不是字符串数组），每项都要有 `id`；
- 顶层要有 `did`；
- `req` 是 `[[0,"any",[0,1]]]`（第一个模板 `"any"`，后面是该卡要用的字段 **ord** 列表）；
- 顶层 `mod`/`usn`/`sortf`/`type`/`latexPre`/`latexPost`/`latexsvg`/`css` 都要给（缺了引擎可能报错）。

### 5.3 `GetConfigJson` 的值是 **JSON 文本**

`GetConfigJson(key)` 返回的是 JSON 编码后的字符串。存 `"hello-config"` 时
`SetConfigJson(key, "\"hello-config\"")`（**要带引号**），读回来也是带引号的 `"hello-config"`。
存对象就传 `{"a":1}` 这样的文本。

### 5.4 `AddMediaFile` 的返回文件名可能被改写

重名时引擎会给新名字，**拿返回值**，不要拿入参。文件落在 `media_folder_path` 下的
`collection.media\`，实测路径是 `<dir>\collection.media\<返回的文件名>`。

### 5.5 没有 `notesInfo` 这种批处理 RPC

引擎侧叫 **note** 不叫 card。要「一批笔记的字段值」只能 `get_note`(25/6) 逐个取，
再配 `get_field_names`(23/16)。`EngineRpc.NotesInfo` 就是这么拼的（N 次 RPC）。
要拿牌组/卡片相关的批量信息得走 `search_cards`(29/1) / `cards_of_note`(25/12)。

### 5.6 未登录时 `sync_status` 会以返回码 2 失败

实测（空 hkey）：`RPC 失败（service=1 method=4，返回码 2）：Please use the Check Database
function, then sync again. …`。说明 **RPC 通道是通的**，只是没凭证。
`EngineRpc.SyncStatus` 会把这种情况抛成 `EngineException`（`Code=2`）。

### 5.7 没有「读回媒体」的 RPC：媒体得直接读磁盘

`BackendMediaService`（41）只有写/查/回收站这一侧的动作（`add_media_file` 41/2、
`trash_media_files` 41/4、`check_media` 41/1、`get_absolute_media_path` 41/9 …），
**没有 `retrieve_media_file` 这类读回接口**。要读回已经写进去的文件，只能直接读磁盘：

```
<media_folder_path>\<文件名>       # 本例：<DataDir>\anki\collection.media\<文件名>
```

`AnkiConn.RetrieveMediaFile(fileName)` 就是这么实现的：`File.ReadAllBytes`，精确名字读不到
再试一次小写名（引擎的 `normalize_filename` 会小写），两个都失败返回 **null**（不抛异常），
因为浏览与头像那边把 `null` 当「没有这个文件」。

配套注意：`AddMediaFile` 在同名但内容不同时会把文件改名成 `stem-<sha1>.ext`（见 5.4），
所以凡是**靠固定文件名做覆盖**的地方（头像就是），写之前必须先把同名旧文件 trash 掉 ——
`AnkiConn.StoreMediaFile` 里是 `TrashOrDeleteLocal(fileName)`，否则文件名一变，下一次就找不回。

### 5.8 收藏库不存在时引擎会自己建；被第二个进程打开时会干净拒绝

`open_collection`（3/0）对**不存在**的 `collection_path` 不报错，会直接建一份新库
（父目录要已存在；`AnkiConn.Open()` 先 `Directory.CreateDirectory(<DataDir>\anki)`）。
所以「打开库失败」基本只有两类原因：

1. dll 不可用 / 路径为空（`Engine.Available == false`，看 `Engine.UnavailableReason`）；
2. 同一份库已经被**别的进程**打开（现实里基本都是「上一个 AnkiAssistant 没退干净」）。

第 2 类实测这样报（两个进程抢同一份库，进程 A 持有中）：

```
RPC 失败（service=3 method=0，返回码 2）：Anki already open, or media currently syncing.
```

**这不是数据损坏**，重试也不会成功；`AnkiConn.Explain` 把它翻成「收藏库正被占用…关掉它
（或重启一次本程序）再试」。退出时必须 `AnkiConn.Close()`（内部 `EngineRpc.CloseCollection()`
+ `Engine.Close()`），否则下次启动就是这个错。`Engine.Opened` 用来判断是否已经打开，别重复 open。
我们的库在 `%APPDATA%\AnkiAssistant\anki\`，和用户真正的 `%APPDATA%\Anki2\` 不是同一份文件。

---

## 6. 怎么自己复核

```powershell
# 1) 编号：直接看生成绑定的方法体
Select-String -Path 'D:\Anki-Android-Backend\anki\out\pylib\anki\_backend_generated.py' `
              -Pattern 'def (open_collection|add_deck|add_note)_raw' -Context 0,4

# 2) 字段号：看 proto
Select-String -Path 'D:\Anki-Android-Backend\anki\proto\anki\notes.proto' -Pattern 'AddNoteRequest' -Context 0,4

# 3) 端到端：跑联机探针（29 项断言，实测 29 PASS / 0 FAIL，退出码 0）
& "$env:TEMP\engine_probe.exe" '<repo>\AnkiAssistant\tools\lib\rslib_aa.dll'
# 完整日志（UTF-8）：%TEMP%\engine_probe.log
```

`src\EngineSelfTest.cs` 里 `engine.rpc.method_table` 那组断言会回归上面所有编号：
改了常量而没同步文档，自检就会红。
