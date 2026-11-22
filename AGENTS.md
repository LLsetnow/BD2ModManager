# 项目协作与发布规则

## Worktree 与分支

- 所有代码改动都在独立的 Git worktree 中进行。每次新建 worktree 时，必须同时创建并检出一个新的专属分支；不要直接在 `main` 上开发。
- 分支类别只能是 `feat`、`fix` 或 `chore`。期望的语义格式是 `<类别>:<内容>`；但 Git 分支名不能包含冒号（`:`），所以实际分支名使用 `<类别>/<内容>`，内容用简短的 kebab-case 描述。例如：`feat/add-update-manifest`、`fix/game-launch`、`chore/update-docs`。
- PR 合并到 `main` 后，删除该改动对应的本地 worktree 目录。

## GitHub 与合并

- 创建 PR 前，抓取目标远程 `main` 的最新提交，并将工作分支 rebase 到该提交；若 `main` 在开 PR 前又有更新，重新抓取并 rebase。
- 所有提交到 GitHub 的改动都必须通过 Pull Request（PR）；禁止直接向 `main` 推送改动。
- 每个 PR 的目标分支必须是 `main`。检查通过并完成审查后，将 PR 合并到 `main`。

## 发布与自动更新

- 自动更新源配置在 `src-tauri/tauri.conf.json`，当前指向 `https://github.com/LLsetnow/BD2ModManager/releases/latest/download/latest.json`。
- 每次发布都必须将有效的 `latest.json` 作为 GitHub Release 附件上传，并同时上传该清单引用的签名更新包及其所需文件。不要只上传安装器。
- 发布前确认上述 `latest.json` 下载地址可访问并返回有效的 Tauri 更新清单，清单中的版本、下载地址和签名与本次发布匹配。缺少该文件会导致应用显示“Could not fetch a valid release JSON from the remote”，自动更新检查失败。
