# Changelog

## 1.6.0

- 再次双击程序会直接唤回已有窗口，包括托盘和最小化状态；首次提醒也能唤回。
- 托盘新增“快速复制收藏”：普通条目直接复制，私密条目先解锁；最多显示 12 个快捷入口，并提供全部收藏入口。
- 托盘复制后短暂显示“已复制”提示；始终按当前时间生成验证码。
- 窗口隐藏或最小化时停止卡片刷新，显示时每秒更新并立即校准验证码。
- 唤回窗口时保留正在编辑的对话框；托盘菜单重新检查条目状态，防止使用已删除、取消收藏或已锁定的旧条目。
- 所有界面语言覆盖新增操作，账户名称保持原文。
- 发布流程从项目读取版本号，并使用上一份已发布安装包验证覆盖升级。
- 保留一键复制、剪贴板不自动清空、五种语言和跟随系统的主题设置。

Reopening the executable restores the existing window. Favorite entries can be
copied from the tray; private entries still require unlocking. Card refreshes stop
while hidden. Regression tests include cross-process activation and upgrading
from a previously published installer before release publication.

## 1.5.0

- Publish AuthLocal for Windows as an MIT-licensed project.
- Fix copied passwords disappearing when the app is minimized or closed to tray.
- Remove all automatic clipboard erasure, including timeouts, locks and exit.
- Add Light / Dark / System appearance; System is the default and follows live changes.
- Add Simplified Chinese, Traditional Chinese, English, French and Japanese; language is adjustable.
- Require acknowledgement of the first-launch warning: do not store important passwords.
- Add a per-user Windows installer with a stable application ID, version and uninstall registration.
- Keep existing LocalAuth portable data and encrypted backup compatibility.
- Keep the compact unified home, one-click copy, private entries and tray operation.

The clipboard defect was reproduced against 1.3.0: `123@1` was copied correctly,
but minimizing the window cleared it before paste. It was not a symbol restriction.
