using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Connect PostgreSQL Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<KnowledgeContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Connect to Groq's Free Cloud API
builder.Services.AddOpenAIChatCompletion(
    modelId: "llama3-8b-8192", 
    apiKey: builder.Configuration["GroqApiKey"] ?? "YOUR_GROQ_API_KEY", 
    endpoint: new Uri("https://groq.com") 
);

builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());
// Simple root landing response to clear Render health-check warnings
app.MapGet("/", () => Results.Text("🤖 Chatbot API Backend is running live!"));

//app.UseHttpsRedirection();

// ENDPOINT A: Update or add details dynamically to the database
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
    return Results.Ok(new { message = "Knowledge database successfully updated!" });
});

// ENDPOINT B: Bot conversation endpoint feeding live Postgres details into Groq
app.MapPost("/api/chat", async (ChatRequest request, KnowledgeContext db, IChatCompletionService chatService) =>
{
    var currentRecords = await db.KnowledgeItems.ToListAsync();
    var contextBuilder = new StringBuilder();
    foreach (var record in currentRecords)
    {
        contextBuilder.AppendLine($"[{record.Category}] {record.TopicName}: {record.ContentDetails}");
    }

    var systemInstruction = 
        "You are an automated support bot. Answer questions accurately using ONLY the context provided below.\n" +
        "If the answer cannot be verified using the context data, reply with: " +
        "'I do not have that specific detail on file. Let me connect you with a team representative.'\n" +
        "Do not make up facts under any circumstances.\n\n" +
        $"[LIVE POSTGRES CONTEXT]\n{contextBuilder}";

    var history = new ChatHistory(systemInstruction);
    history.AddUserMessage(request.UserQuery);

    var response = await chatService.GetChatMessageContentAsync(history);
    return Results.Ok(new { botResponse = response.Content });
});

app.Run();

public record ChatRequest(string UserQuery);
