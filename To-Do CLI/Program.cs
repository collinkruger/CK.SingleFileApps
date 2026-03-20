// -----------
// Style Guide
// -----------
//
// Structure:
// - Top-level statements; no wrapping class or namespace.
// - Local functions for command handlers and helpers.
// - Separate logical sections with '// --- Section Name ---' banners.
// - Records for DTOs; use [JsonPropertyName] on each property.
// - HTTP helpers return (int Status, string Body) tuples.
//
// Naming:
// - PascalCase for methods, types, and constants; camelCase for locals and parameters.
// - Use 'var' for all local variable declarations.
//
// Formatting — switch expressions:
// - Align '=>' across arms by padding the pattern with spaces.
// - Multi-line arms (e.g. with a nested ternary) indent the continuation
//   lines to align with the '=>' column.
//   Example:
//       "lists"    => condition
//                     ? branchA
//                     : branchB,
//       "tasks"    => singleExpr,
//       _          => fallback
//
// Formatting — ternary operators:
// - Always split across three lines; never on one line.
// - Place '?' and ':' at the start of continuation lines,
//   indented to align under the value being assigned or returned.
//   Example:
//       return status != 200
//              ? ApiError(status, body)
//              : JsonOut(body);
//
// Formatting — general:
// - Expression-bodied members (=>) for one-liner functions.
// - Omit braces on single-statement if/foreach bodies.
// - Blank lines between logical steps within a function.
// - Long arguments wrap with continuation aligned to the opening '('.

using Azure.Identity;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string GraphBaseUrl = "https://graph.microsoft.com/v1.0/me/todo/lists";
const string ClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e"; // Microsoft Graph PowerShell app ID — pre-registered by Microsoft, no app registration needed
const string TenantId = "common";
string[] Scopes = ["Tasks.ReadWrite"];

var authRecordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                  ".todo-cli-auth");

// Load saved AuthenticationRecord if it exists, so silent auth works across runs
AuthenticationRecord? authRecord = null;
if (File.Exists(authRecordPath))
{
    using var stream = File.OpenRead(authRecordPath);
    authRecord = await AuthenticationRecord.DeserializeAsync(stream);
}

var credentialOptions = new InteractiveBrowserCredentialOptions
{
    ClientId = ClientId,
    TenantId = TenantId,
    TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "todo-cli" }
};

if (authRecord != null)
    credentialOptions.AuthenticationRecord = authRecord;

var credential = new InteractiveBrowserCredential(credentialOptions);

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Parse --json flag from args
var argList = args.ToList();
bool jsonOutput = argList.Remove("--json");

if (argList.Count == 0)
    return PrintUsage();

try
{
    return argList[0] switch
    {
        "lists"    => argList.Count >= 2 && argList[1] == "create"
                      ? await CreateList(argList.Skip(2).ToArray())
                      : await ListLists(),
        "tasks"    => await ListTasks(argList.Skip(1).ToArray()),
        "add"      => await AddTask(argList.Skip(1).ToArray()),
        "complete" => await CompleteTask(argList.Skip(1).ToArray()),
        "delete"   => await DeleteTask(argList.Skip(1).ToArray()),
        "logout"   => Logout(),
        _          => PrintUsage()
    };
}
catch (AuthenticationFailedException ex)
{
    return Error($"Authentication failed: {ex.Message}");
}
catch (HttpRequestException ex)
{
    return Error($"API error: {ex.Message}");
}

// --- Command Handlers ---

async Task<int> ListLists()
{
    if (jsonOutput)
    {
        var (status, body) = await GraphGetAsync(GraphBaseUrl);
        
        return status != 200
               ? ApiError(status, body)
               : JsonOut(body);
    }

    var (s, items) = await GraphGetAllAsync<TaskListDto>(GraphBaseUrl);
    
    if (s != 200)
        return Error($"HTTP {s}");

    foreach (var list in items)
        Row(list.Id, list.DisplayName);
    
    return 0;
}

async Task<int> CreateList(string[] a)
{
    if (a.Length < 1)
        return UsageError("lists create <name>");
    
    var payload = JsonSerializer.Serialize(new { displayName = a[0] }, jsonOptions);
    
    var (status, body) = await GraphPostAsync(GraphBaseUrl, payload);
    
    if (status != 201)
        return ApiError(status, body);
    
    if (jsonOutput)
        return JsonOut(body);

    var list = JsonSerializer.Deserialize<TaskListDto>(body, jsonOptions)!;

    Row(list.Id, list.DisplayName);
    
    return 0;
}

async Task<int> ListTasks(string[] a)
{
    if (a.Length < 1)
        return UsageError("tasks <listId>");
    
    var url = $"{GraphBaseUrl}/{a[0]}/tasks";

    if (jsonOutput)
    {
        var (status, body) = await GraphGetAsync(url);
        
        return status != 200
               ? ApiError(status, body)
               : JsonOut(body);
    }

    var (s, items) = await GraphGetAllAsync<TaskDto>(url);
    
    if (s != 200) 
        return Error($"HTTP {s}");

    foreach (var t in items)
    {
        var due = t.DueDateTime?.DateTime is string d
                  ? d[..10]
                  : "";
        
        var status2 = t.Status == "completed"
                      ? "done"
                      : "todo";
        
        Row(t.Id, status2, due, t.Title);
    }
    return 0;
}

async Task<int> AddTask(string[] a)
{
    if (a.Length < 2)
        return UsageError("add <listId> <title> [--due YYYY-MM-DD]");
    
    var listId = a[0];
    var title = a[1];

    var taskObj = new Dictionary<string, object> { ["title"] = title };

    var dueIdx = Array.IndexOf(a, "--due");
    if (dueIdx >= 0 && dueIdx + 1 < a.Length)
    {
        taskObj["dueDateTime"] = new { dateTime = a[dueIdx + 1] + "T00:00:00", timeZone = "UTC" };
    }

    var payload = JsonSerializer.Serialize(taskObj, jsonOptions);
    var url = $"{GraphBaseUrl}/{listId}/tasks";
    
    var (status, body) = await GraphPostAsync(url, payload);
    
    if (status != 201)
        return ApiError(status, body);
    
    if (jsonOutput)
        return JsonOut(body);

    var t = JsonSerializer.Deserialize<TaskDto>(body, jsonOptions)!;

    Row(t.Id, t.Title);
    
    return 0;
}

async Task<int> CompleteTask(string[] a)
{
    if (a.Length < 2)
        return UsageError("complete <listId> <taskId>");
    
    var url = $"{GraphBaseUrl}/{a[0]}/tasks/{a[1]}";
    var payload = JsonSerializer.Serialize(new { status = "completed" }, jsonOptions);
    
    var (status, body) = await GraphPatchAsync(url, payload);
    
    if (status != 200)
        return ApiError(status, body);
    
    return jsonOutput
           ? JsonOut(body)
           : Ok();
}

async Task<int> DeleteTask(string[] a)
{
    if (a.Length < 2)
        return UsageError("delete <listId> <taskId>");
    
    var url = $"{GraphBaseUrl}/{a[0]}/tasks/{a[1]}";
    var (status, body) = await GraphDeleteAsync(url);
    
    if (status != 204)
        return ApiError(status, body);
    
    return jsonOutput
           ? JsonOut("{}")
           : Ok();
}

int Logout()
{
    var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                ".IdentityService");
    
    var cachePath = Path.Combine(cacheDir, "todo-cli.nocae");

    if (File.Exists(authRecordPath))
        File.Delete(authRecordPath);

    if (File.Exists(cachePath))
        File.Delete(cachePath);

    return jsonOutput
           ? JsonOut("{}")
           : Ok("Logged out.");
}

// --- Auth & HTTP Helpers ---

async Task<string> GetAccessTokenAsync()
{
    if (authRecord == null)
    {
        // First run — authenticate interactively and save the record for future silent auth
        authRecord = await credential.AuthenticateAsync(new Azure.Core.TokenRequestContext(Scopes));
        
        using var stream = File.Create(authRecordPath);
        
        await authRecord.SerializeAsync(stream);
    }
    
    var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(Scopes));
    
    return token.Token;
}

async Task<(int Status, string Body)> GraphGetAsync(string url)
{
    using var client = new HttpClient();
    
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());
    
    var resp = await client.GetAsync(url);
    
    return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<(int Status, List<T> Items)> GraphGetAllAsync<T>(string url)
{
    var allItems = new List<T>();
 
    string? nextUrl = url;
    while (nextUrl != null)
    {
        var (status, body) = await GraphGetAsync(nextUrl);
        
        if (status != 200)
            return (status, allItems);
        
        var page = JsonSerializer.Deserialize<GraphResponse<T>>(body, jsonOptions)!;
        allItems.AddRange(page.Value);
        
        nextUrl = page.NextLink;
    }

    return (200, allItems);
}

async Task<(int Status, string Body)> GraphPostAsync(string url, string jsonPayload)
{
    using var client = new HttpClient();
    
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());
    
    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
    
    var resp = await client.PostAsync(url, content);
    
    return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<(int Status, string Body)> GraphPatchAsync(string url, string jsonPayload)
{
    using var client = new HttpClient();
    
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());
    
    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
    
    var resp = await client.PatchAsync(url, content);
    
    return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<(int Status, string Body)> GraphDeleteAsync(string url)
{
    using var client = new HttpClient();
    
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());
    
    var resp = await client.DeleteAsync(url);
    
    var body = resp.Content.Headers.ContentLength > 0
               ? await resp.Content.ReadAsStringAsync()
               : "";
    
    return ((int)resp.StatusCode, body);
}

// --- Output Helpers ---

string Pretty(string json)
{
    using var doc = JsonDocument.Parse(json);
    return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
}

int JsonOut(string body) { Console.WriteLine(Pretty(body)); return 0; }

int UsageError(string usage) { Error($"Usage: todo {usage}"); return 2; }

void Row(params string[] fields) => Console.WriteLine(string.Join('\t', fields));

int Ok(string msg = "OK") { Console.WriteLine(msg); return 0; }

int ApiError(int status, string body) => Error($"HTTP {status}: {body}");

int Error(string msg) { Console.Error.WriteLine($"error: {msg}"); return 1; }

int PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: todo <command> [options]

        Commands:
          lists                          List all task lists
          lists create <name>            Create a new task list
          tasks <listId>                 List tasks in a list
          add <listId> <title> [--due YYYY-MM-DD]
                                         Add a task
          complete <listId> <taskId>     Mark task completed
          delete <listId> <taskId>       Delete a task
          logout                         Clear cached auth tokens

        Flags:
          --json                         Output raw JSON from Graph API

        Auth: Opens browser for Microsoft login on first use. Token is cached.
        """);
    return 2;
}

// --- Data Models ---

record GraphResponse<T>(
    [property: JsonPropertyName("value")] List<T> Value,
    [property: JsonPropertyName("@odata.nextLink")] string? NextLink
);

record TaskListDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string DisplayName
);

record TaskDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("dueDateTime")] DueDateDto? DueDateTime
);

record DueDateDto(
    [property: JsonPropertyName("dateTime")] string? DateTime,
    [property: JsonPropertyName("timeZone")] string? TimeZone
);
