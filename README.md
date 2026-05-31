# Tissue for Jellyfin

Tissue for Jellyfin 是 [Tissue](https://github.com/chris-2s/tissue) 的 Jellyfin 插件，用于让 Jellyfin 通过 Tissue 获取演员头像信息。

> [Tissue](https://github.com/chris-2s/tissue) 是一款独立的刮削工具；本项目不是 Tissue 主程序，而是用于连接 Jellyfin 与 Tissue 的插件。

## 功能特性

- 为 Jellyfin 演员补全头像
- 通过 Tissue API Key 调用 Tissue 服务
- 支持按媒体库范围启用，避免对不相关媒体库发起请求
- 适用于 Jellyfin 10.9.x+

## 安装

在 Jellyfin 后台进入：

```text
控制台 → 插件 → 管理存储库
```

添加插件源：

```text
https://chris-2s.github.io/jellyfin-plugin-tissue/manifest.json
```

保存后进入：

```text
控制台 → 插件 → 全部
```

找到 `Tissue` 并安装。安装完成后重启 Jellyfin。

<img width="1846" height="1356" alt="image" src="https://github.com/user-attachments/assets/0623bec7-1508-49c4-9755-2537d0938710" />

<img width="2354" height="1334" alt="image" src="https://github.com/user-attachments/assets/ab075ede-ca66-4862-a8bd-8aa7683934bd" />

## 获取 Tissue API Key

使用本插件前，需要先在 [Tissue](https://github.com/chris-2s/tissue) 中获取 API Key。

1. 更新最新版本Tissue。
2. 进入用户管理页面。
3. 创建 API Key。
4. 保存该 API Key，稍后填入 Jellyfin 插件配置中。

<img width="2818" height="1324" alt="image" src="https://github.com/user-attachments/assets/bef68c87-40c9-4892-a3f1-d202c05e1802" />

## 配置插件

在 Jellyfin 后台进入：

```text
控制台 → 插件 → 已安装 → Tissue
```

填写插件配置：

- `服务器地址`：Tissue的API地址
- `API密钥`：Tissue生成的API Key
- `允许生效的媒体库`：选择需要使用 Tissue 补全演员头像的媒体库

保存配置后，建议重启 Jellyfin 或重新扫描相关媒体库。

<img width="1712" height="968" alt="image" src="https://github.com/user-attachments/assets/5c00f1bb-1b59-4748-996d-02a13aff0849" />

## 使用效果

配置完成后，当 Jellyfin 请求演员头像且本地没有可用图片时，Tissue for Jellyfin 会通过 Tissue 查询并返回对应的演员头像。

<img width="2340" height="1302" alt="image" src="https://github.com/user-attachments/assets/24c3c344-ebd2-4e7a-8d2b-d49595fb767f" />

## 常见问题

### Tissue for Jellyfin 和 Tissue 是什么关系？

[Tissue](https://github.com/chris-2s/tissue) 是独立的刮削工具，负责提供演员等元数据能力。

Tissue for Jellyfin 是 Jellyfin 插件，作用是在 Jellyfin 中调用 Tissue，让 Jellyfin 可以使用 Tissue 的演员头像数据。

### 安装后插件没有生效

请确认：

- Jellyfin 已经重启
- 安装目录中存在 `Jellyfin.Plugin.Tissue.dll`
- Jellyfin 版本兼容当前插件
- 插件配置中已经填写正确的 Tissue API Key

### 修改配置后没有立即生效

可以尝试：

- 保存配置后重启 Jellyfin
- 重新扫描相关媒体库
- 清理已有演员图片缓存后再测试
