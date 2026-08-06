using Microsoft.Extensions.DependencyInjection;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;
using QianYuan.Data;
using QianYuan.Data.Services;
using QianYuan.Kernel;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var dbPath = Path.Combine(root, ".runtime", "qianyuan-multiturn-smoke.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
if (File.Exists(dbPath)) File.Delete(dbPath);

var services = new ServiceCollection();
services.AddLogging();
services.AddQianYuanData($"DataSource={dbPath}", AesEncryptionService.GenerateEncryptionKey(), "Sqlite");
services.AddQianYuanKernel();
await using var provider = services.BuildServiceProvider();
await provider.InitializeDatabaseAsync();

using var scope = provider.CreateScope();
var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
if (store.GetType().Name != "EfSessionStore")
{
    throw new InvalidOperationException($"ISessionStore should be EfSessionStore, got {store.GetType().FullName}");
}

var sessionId = "smoke-" + Guid.NewGuid().ToString("N");
var state = new SessionState
{
    SessionId = sessionId,
    OwnerId = "smoke-owner",
    Title = "Smoke 会话",
    AgentId = "qianyuan.default",
    Metadata = new Dictionary<string, string> { ["source"] = "smoke" },
};
state.Messages.Add(ChatMessage.User("第一条用户消息"));
state.Messages.Add(ChatMessage.Assistant(new[] { ContentPart.FromText("助手回复"), ContentPart.ToolCall("call-1", "demo.tool", "{\"x\":1}") }));
state.Messages.Add(ChatMessage.Tool("call-1", "{\"ok\":true}", "工具结果"));
await store.SaveAsync(state);

var restored = await store.GetAsync(sessionId) ?? throw new InvalidOperationException("Session not restored");
if (restored.Messages.Count != 3) throw new InvalidOperationException($"Expected 3 messages, got {restored.Messages.Count}");
if (restored.Messages[1].Parts.Count != 2) throw new InvalidOperationException("Assistant parts were not restored");
if (restored.Messages[2].Parts[0].Kind != ContentKind.ToolResult) throw new InvalidOperationException("Tool result kind was not restored");

var list = await store.ListAsync("smoke-owner");
if (!list.Any(s => s.SessionId == sessionId && s.MessageCount == 3)) throw new InvalidOperationException("Session list missing saved conversation");

restored.Title = "重命名会话";
await store.SaveAsync(restored);
var renamed = await store.GetAsync(sessionId) ?? throw new InvalidOperationException("Renamed session not restored");
if (renamed.Title != "重命名会话") throw new InvalidOperationException("Rename was not persisted");

await store.DeleteAsync(sessionId);
if (await store.GetAsync(sessionId) is not null) throw new InvalidOperationException("Deleted session is still visible");

Console.WriteLine("Session smoke passed");
