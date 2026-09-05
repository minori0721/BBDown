# BBDown for Bili-favorites-backup bfb-2.0.5

## 修复

- 互动视频不再使用已失效的 `/x/player.so`；通过 player 接口读取剧情版本并遍历接口返回的可达分支。
- 按 `(CID, edge ID)` 防止循环，媒体按 CID 去重；限制 500 节点、5000 个选择、90 秒总预算，异常时不返回半份剧情清单。
- 新增集成用片段清单协议 `--bfb-pages-json` 和下载清单绑定参数 `--bfb-page-set-sha256`，分P/CID顺序变化时在下载前停止。
- 互动媒体探测提供完整清单摘要，支持BFB拒绝缺失片段的大小合计。摘要仅针对CID列表，不计算视频内容哈希。
- 剧情权限、限流、超时和格式错误使用独立安全错误码，不当作视频已删除；普通视频不增加剧情查询。

## 范围与验证

- 仅归档接口返回的可达视频片段，不重建互动玩法、条件变量或隐藏剧情，也不保证源站提供每个片段的媒体流。
- WSL2 Release 配置单元测试：61通过、0失败；Linux x64 Native AOT 发布构建及版本冒烟检查通过。
- BFB本地配套测试：567通过、2环境跳过；CLI UI三视口102通过、63条件跳过。BFB需另行更新固定版本，本Release不会自动部署或重试服务器任务。
- 正式产物由现有 GitHub Actions 在 `.NET SDK 9.0.306 / Debian Bookworm` 构建，附带 ZIP、SHA256 和来源提交信息。

不发布 NuGet 或 GitHub Packages，不改动历史 Release 资产。
