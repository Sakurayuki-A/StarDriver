# 字体文件说明

## 如何添加 Lexend Deca 字体

### 步骤 1：下载字体
1. 访问 Google Fonts: https://fonts.google.com/specimen/Lexend+Deca
2. 点击 "Download family" 下载字体包
3. 解压后找到 `LexendDeca-Regular.ttf` 文件

### 步骤 2：添加到项目
1. 将 `LexendDeca-Regular.ttf` 复制到此文件夹（`StarDriver.UI/Fonts/`）
2. 如果需要其他字重，也可以添加：
   - `LexendDeca-Bold.ttf`
   - `LexendDeca-SemiBold.ttf`
   - 等等

### 步骤 3：设置文件属性
在 Visual Studio 中：
1. 右键点击字体文件
2. 选择 "属性"
3. 设置 "生成操作" 为 "Resource"
4. 设置 "复制到输出目录" 为 "不复制"

或者手动编辑 `StarDriver.UI.csproj`，添加：
```xml
<ItemGroup>
  <Resource Include="Fonts\LexendDeca-Regular.ttf" />
  <Resource Include="Fonts\LexendDeca-Bold.ttf" />
  <Resource Include="Fonts\LexendDeca-SemiBold.ttf" />
</ItemGroup>
```

### 步骤 4：验证
运行项目，字体应该自动应用到所有文本。

## 字体许可
Lexend Deca 使用 SIL Open Font License 1.1，可以免费用于商业和非商业项目。
