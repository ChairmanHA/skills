# Git 标签重建 + 强推 + 切换/切回流程

适用场景：
- 本地把同名标签改到了新提交，但远端没有被覆盖（普通 `git push` 不会覆盖已存在标签）。
- 需要把远端标签强制指向目标提交，并验证结果。
- 需要临时切到标签检查代码，再切回原分支。

## 一次性可执行命令（推荐）

```bash
# 0) 进入仓库
cd /home/jiashilin/vsg2.0/sgstudio

# 1) 准备变量
TAG_NAME="v2.6.1"
TARGET_SHA="8af5f54e9478ed5c042c28713ea376a4d233e4df"
REMOTE_URL="http://<git_user>:<git_password>@192.168.3.29:8089/rdd/sgstudio.git"

# 2) 记录当前现场（用于切回）
ORIG_BRANCH=$(git rev-parse --abbrev-ref HEAD)
ORIG_COMMIT=$(git rev-parse HEAD)
echo "ORIG_BRANCH=$ORIG_BRANCH"
echo "ORIG_COMMIT=$ORIG_COMMIT"

# 3) 查看修复前的本地/远端标签指向
echo "== BEFORE LOCAL =="
git rev-parse "$TAG_NAME" || true
git rev-parse "$TAG_NAME^{}" || true
echo "== BEFORE REMOTE =="
git ls-remote --tags "$REMOTE_URL" "$TAG_NAME" "$TAG_NAME^{}"

# 4) 本地重建标签到目标提交（轻量标签）
git tag -f "$TAG_NAME" "$TARGET_SHA"

# 5) 本地确认
echo "== AFTER LOCAL =="
git rev-parse "$TAG_NAME"
git rev-parse "$TAG_NAME^{}"

# 6) 强制更新远端同名标签
git push --force "$REMOTE_URL" "refs/tags/$TAG_NAME"

# 7) 远端确认
echo "== AFTER REMOTE =="
git ls-remote --tags "$REMOTE_URL" "$TAG_NAME" "$TAG_NAME^{}"

# 8) 切到标签核对（detached HEAD）
git checkout --detach "$TAG_NAME"
git rev-parse HEAD

# 9) 切回原分支
git checkout "$ORIG_BRANCH"
git rev-parse --abbrev-ref HEAD
git rev-parse HEAD
```

## 分步说明

1. `git tag -f <tag> <sha>`
- 作用：在本地强制把标签名重新指向目标提交。
- 注意：这一步只改本地，不会自动改远端。

2. `git push --force <remote> refs/tags/<tag>`
- 作用：覆盖远端已存在同名标签。
- 原因：普通 `git push` 不覆盖已有标签。

3. `git ls-remote --tags <remote> <tag> <tag^{}>`
- 作用：验证远端标签最终指向。
- 说明：
  - 轻量标签通常只返回 `<tag>` 一条。
  - 注释标签通常返回 `<tag>`（标签对象）和 `<tag^{}>`（解引用提交）两条。

4. `git checkout --detach <tag>`
- 作用：切到标签对应提交做只读核对，避免误提交到分支。

5. `git checkout <orig_branch>`
- 作用：回到原分支继续开发。

## 可选：如果你想保留“注释标签”形式

```bash
# 删除本地旧标签
git tag -d "$TAG_NAME"

# 重新创建注释标签（会生成新的标签对象 SHA）
git tag -a "$TAG_NAME" "$TARGET_SHA" -m "retag $TAG_NAME to $TARGET_SHA"

# 强推远端
git push --force "$REMOTE_URL" "refs/tags/$TAG_NAME"

# 验证（通常会出现两条：tag 对象 + peeled commit）
git ls-remote --tags "$REMOTE_URL" "$TAG_NAME" "$TAG_NAME^{}"
```

## 安全建议

- 不要把明文密码写进长期文档或脚本，优先使用凭据管理器。
- 如果必须临时使用 URL 带密码，执行后建议清理 shell 历史或改用交互式认证。
