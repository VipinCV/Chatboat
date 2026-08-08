using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using OpenAI; 
using OpenAI.Chat; 
using System.ClientModel; 

var builder = WebApplication.CreateBuilder(args);

// 1. Setup Postgres Connection Configuration
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"] 
                    ?? builder.Configuration["DefaultConnection"]
                    ?? Environment.GetEnvironmentVariable("DefaultConnection");

builder.Services.AddDbContext<KnowledgeContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Register standard Semantic Kernel engine structure
builder.Services.AddKernel();

// Enable global permissive CORS parameters for frontend widget integrations
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Root endpoint to resolve Render health check alerts
app.MapGet("/", () => Results.Text("🤖 Chatbot API Backend is running live!"));

// ENDPOINT A: Dynamic Database Updates
app.MapPost("/api/knowledge/update", async (KnowledgeItem item, KnowledgeContext db) =>
{
    var existing = await db.KnowledgeItems
        .FirstOrDefaultAsync(x => x.Category == item.Category && x.TopicName == item.TopicName);

    if (existing != null)
    {
        existing.ContentDetails = item.ContentDetails;
        existing.LastUpdated = DateTime.UtcNow;
    }
    else
    {
        db.KnowledgeItems.Add(item);
    }
    
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Knowledge base successfully updated!" });
});

// ==========================================
// ENDPOINT B: PRODUCTION-GRADE GROQ CHAT ENGINE
// ==========================================
app.MapPost("/api/chat", async (ChatRequest request, KnowledgeContext db) =>
{
    try
    {
        // 1. Fetch live records from Neon PostgreSQL
        var currentRecords = await db.KnowledgeItems.ToListAsync();
        var contextBuilder = new StringBuilder();
        foreach (var record in currentRecords)
        {
            contextBuilder.AppendLine($"[{record.Category}] {record.TopicName}: {record.ContentDetails}");
        }

        // 2. Build the system instruction rulebook constraints
        var systemInstruction = 
            "You are an automated support bot. Answer questions accurately using ONLY the context provided below.\n" +
            "If the answer cannot be verified using the context data, reply with: " +
            "'I do not have that specific detail on file. Let me connect you with a team representative.'\n" +
            "Do not make up facts under any circumstances.\n\n" +
            $"[LIVE NEON DB CONTEXT]\n{contextBuilder}";

        // 3. Extract your Groq API token key safely
        var key = builder.Configuration["GroqApiKey"] ?? Environment.GetEnvironmentVariable("GroqApiKey");

        if (string.IsNullOrWhiteSpace(key) || key == "YOUR_GROQ_API_KEY")
        {
            return Results.Ok(new { botResponse = "⚠️ Configuration Alert: GroqApiKey is missing from the Render dashboard environment variables." });
        }
        
        // 4. Formulate the official JSON payload structure
        var payload = new
        {
            model = "llama3-8b-8192",
            messages = new[]
            {
                new { role = "system", content = systemInstruction },
                new { role = "user", content = request.UserQuery }
            },
            temperature = 0.1
        };

        var jsonContent = JsonSerializer.Serialize(payload);
        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        using var client = new HttpClient();
        
        // Apply authorization header rules cleanly
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        // 5. Send payload directly to the official OpenAI compatibility URL path
        var response = await client.PostAsync("https://groq.com", httpContent);
        
        if (!response.IsSuccessStatusCode)
        {
            var rawError = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROQ API CONSOLE FAIL]: Status {response.StatusCode} -> Detail: {rawError}");
            return Results.Ok(new { botResponse = $"⚠️ Groq API Error response: {response.StatusCode}" });
        }

        // 6. Safely traverse the returning OpenAI-compatible array structure
        var responseBody = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(responseBody);
        
        // Navigate through choices array index [0] to extract the text content string
        var choicesElement = jsonDoc.RootElement.GetProperty("choices");
        var firstChoice = choicesElement[0]; 
        var botMessage = firstChoice.GetProperty("message").GetProperty("content").GetString();

        return Results.Ok(new { botResponse = botMessage });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CRITICAL PIPELINE ENGINE FAILURE]: {ex.Message}");
        return Results.Ok(new { botResponse = $"⚠️ Internal engine error: {ex.Message}" });
    }
});




app.Run();

public record ChatRequest(string UserQuery);
