# Supabase 保活

这是从独立的 `SupabaseKeepAliveTool` 迁入并重构的 SoftwareToolkit 工具。它保留多项目、立即执行、每日/间隔执行和运行日志能力，同时做了以下调整：

- 密码使用当前 Windows 用户的 DPAPI 加密，不再写入浏览器 `localStorage` 或明文 JSON。
- 自动保活使用 Windows 计划任务按次运行，不再维持常驻 PowerShell 进程。
- 后台任务有互斥锁，重复触发时会安全跳过；单次执行最多运行 10 分钟。
- 拒绝 `sb_secret_...` 和可识别的旧版 `service_role` JWT，只接受 publishable/anon key。
- 配置采用临时文件替换写入；日志达到 512 KB 后自动轮转。
- 可直接导入原 Unity、网页或 Windows 版本的 JSON 配置。

## 使用

1. 在 SoftwareToolkit 中启动“Supabase 保活”。
2. 填写一个权限最小化的专用用户邮箱、密码，以及保活表名。
3. 添加项目 URL 和该项目的 publishable key（旧项目也可使用 anon key）。
4. 先点“立即执行”确认成功；需要自动运行时，勾选“启用自动保活”并点“保存并应用计划”。

自动计划统一注册在 Windows 任务目录 `\SoftwareToolkit\`，任务名为 `Supabase KeepAlive`。它会直接显示在 SoftwareToolkit 的“定时任务管理器”里，可在那里立即运行、启用/禁用或删除；保活界面的“任务管理器”按钮也可直接打开该工具。旧版根目录任务会在首次应用计划时自动迁移，避免重复执行。

配置和日志位于 `%LOCALAPPDATA%\SoftwareToolkit\SupabaseKeepAlive`。停用计划任务不会删除配置和日志。

## 建议的表和 RLS

每个目标项目都需准备同名保活表。以下示例与工具默认的 `test` 表匹配，并只允许登录用户插入属于自己的记录：

```sql
create table if not exists public.test (
  id bigint generated always as identity primary key,
  owner_id uuid not null default auth.uid(),
  created_at timestamptz not null default now(),
  change_time timestamptz not null default now()
);

alter table public.test enable row level security;
grant insert on table public.test to authenticated;

create policy "keepalive users insert their own rows"
on public.test
for insert
to authenticated
with check ((select auth.uid()) = owner_id);
```

如果项目关闭了 `public` schema 的 Data API 暴露，还需在 Dashboard 的 Data API 设置中显式暴露该表。不要为解决权限问题使用 secret/service_role key。

## 安全说明

- DPAPI 密文只能由保存它的同一 Windows 用户解密，因此计划任务也以该用户的交互式身份运行。
- publishable/anon key 并非秘密，但它能访问的范围仍由表授权和 RLS 决定。
- 专用用户只能被授予保活表的最小 INSERT 权限，不要复用管理员账号。
- 保活操作会持续新增少量记录；如需控制表大小，可另行设置保留策略。

Supabase 免费项目是否暂停最终由平台的活动判定决定；本工具用于产生正常的用户数据库活动，不承诺绕过平台策略。
