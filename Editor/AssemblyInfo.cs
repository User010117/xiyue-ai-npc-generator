using System.Runtime.CompilerServices;

// 仅向包内 Editor 测试开放内部请求 DTO，避免为了验证 JSON 契约扩大正式公共 API。
[assembly: InternalsVisibleTo("Xiyue.AINpcGenerator.Tests.Editor")]
