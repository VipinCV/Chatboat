using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// 1. SETUP POSTGRES CONNECTION CONFIGURATION
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"] 
                    ?? builder.Configuration["DefaultConnection"]
                    ?? Environment.GetEnvironmentVariable("DefaultConnection");

builder.Services.AddDbContext<KnowledgeContext>(options =>
    options.UseNpgsql(connectionString));

// Enable global permissive CORS parameters for frontend widget integrations
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Root endpoint to resolve Render health check alerts
app.MapGet("/", () => Results.Text("🤖 Chatbot API Backend is running live!"));

// ==========================================
// ENDPOINT A: DYNAMIC DATABASE UPDATES
// ==========================================
app.MapPost("/api/knowledge/update", async (KnowledgeItem item, KnowledgeContext db) =>
{
    try
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
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB UPDATE ERROR]: {ex.Message}");
        return Results.Problem($"Database sync failure: {ex.Message}");
    }
});

// ==========================================
// ENDPOINT B: OPTIMIZED DIRECT JSON RAG COUPLING
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
            return Results.Ok(new { botResponse = "⚠️ Configuration Alert: GroqApiKey is missing from the Render environment variables." });
        }
        
        // 4. Formulate the official JSON payload structure
       var payload = new
{
    model = "openai/gpt-oss-20b", // 👈 FIX APPLIED HERE: Replaced decommissioned model identifier
    messages = new[]
    {
        new { role = "system", content = systemInstruction },
        new { role = "user", content = request.UserQuery }
    },
    temperature = 0.1
};

        // 5. Serialize data using .NET Web Standard JSON formatting rules
        var jsonContent = JsonSerializer.Serialize(payload);
        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        using var client = new HttpClient();
        
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        // 6. Direct HTTP transaction out to the official Groq cloud gateway location
        var response = await client.PostAsync("https://groq.com", httpContent);
        
        if (!response.IsSuccessStatusCode)
        {
            var rawError = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROQ CONSOLE ERROR]: {response.StatusCode} -> {rawError}");
            return Results.Ok(new { botResponse = $"⚠️ Groq API Connection Error: {response.StatusCode}" });
        }

        // 7. Extract string safely from OpenAI JSON schema array structure
        var responseBody = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(responseBody);
        
        var choices = jsonDoc.RootElement.GetProperty("choices");
        
        // Target the first elements array item to extract message contents string
        var firstChoice = choices[0];
        var botMessage = firstChoice.GetProperty("message").GetProperty("content").GetString();

        return Results.Ok(new { botResponse = botMessage });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CRITICAL PIPELINE ENGINE FAILURE]: {ex.Message}");
        return Results.Ok(new { botResponse = $"⚠️ Internal processing engine runtime anomaly: {ex.Message}" });
    }
});

app.Run();

public record ChatRequest(string UserQuery);
