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

// ENDPOINT B: Production-ready direct JSON payload integration for Groq Cloud
app.MapPost("/api/chat", async (ChatRequest request, KnowledgeContext db, IHttpClientFactory httpClientFactory) =>
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

        // 3. 👇 FIX APPLIED HERE: Formulate direct standard JSON to eliminate client routing layers completely
        var key = builder.Configuration["GroqApiKey"] ?? Environment.GetEnvironmentVariable("GroqApiKey") ?? "YOUR_GROQ_API_KEY";
        
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

        // Serialize data and configure the network post client manually
        var jsonContent = System.Text.Json.JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        // Send direct, un-altered request straight to the official compatibility URL gateway path
        var response = await client.PostAsync("https://groq.com", httpContent);
        
        if (!response.IsSuccessStatusCode)
        {
            var rawError = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROQ GATEWAY ERROR CODE]: {response.StatusCode} -> {rawError}");
            return Results.Ok(new { botResponse = $"⚠️ Groq API Error response: {response.StatusCode}" });
        }

        // Parse out the generated text cleanly from the returning stream package
        var responseBody = await response.Content.ReadAsStringAsync();
        using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseBody);
        var choices = jsonDoc.RootElement.GetProperty("choices");
        var botMessage = choices[0].GetProperty("message").GetProperty("content").GetString();

        return Results.Ok(new { botResponse = botMessage });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CRITICAL DIRECT CHAT ERROR]: {ex.Message}");
        return Results.Ok(new { botResponse = $"⚠️ Processing Error occurred: {ex.Message}" });
    }
});



app.Run();

public record ChatRequest(string UserQuery);
