using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Rewrite;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<ITaskService>(new InMemoryTaskService());

var app = builder.Build();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.MapOpenApi();
app.MapScalarApiReference();

app.UseRewriter(new RewriteOptions().AddRedirect("tasks/(.*)", "todos/$1"));

app.Use(async (context, next) =>
{
    Console.WriteLine($"{context.Request.Method} {context.Request.Path} {DateTime.UtcNow} Started");
    await next(context);
    Console.WriteLine($"{context.Request.Method} {context.Request.Path} {DateTime.UtcNow} Finished");
});

app.MapGet("/todos/", (ITaskService service) => service.GetTodos());

app.MapDelete("/todos/", (ITaskService service) =>
{
    service.DeleteTodos();
    return TypedResults.NoContent();
});

app.MapGet("/todos/{id}", Results<Ok<Todo>, NotFound> (int id, ITaskService service) => 
{
    var targetTodo = service.GetTodoById(id);
    return targetTodo is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(targetTodo);
});

app.MapDelete("/todos/{id}", Results<NotFound, NoContent> (int id, ITaskService service) =>
{
    var existing = service.GetTodoById(id);
    if (existing is null) return TypedResults.NotFound();

    service.DeleteTodoById(id);    
    return TypedResults.NoContent();
});
app.MapPut("/todos/{id}", Results<Ok<Todo>, NotFound> (int id, Todo task, ITaskService service) =>
{
    var updated = service.UpdateTodo(id, task);
    return updated is null ? TypedResults.NotFound() : TypedResults.Ok(updated);
});

app.MapPost("/todos/", (Todo task, ITaskService service) =>
{
    var createdTask = service.AddTodo(task);
    return TypedResults.Created($"/todos/{createdTask.Id}", createdTask);
})
.AddEndpointFilter(async (context, next) =>
{
    var taskArgument = context.GetArgument<Todo>(0);
    var errors = new Dictionary<string, string[]>();

    if (taskArgument.DueDate < DateTime.UtcNow)
    {
        errors.Add(nameof(Todo.DueDate), ["Cannot have due date in the past"]);
    }
    if (taskArgument.IsCompleted)
    {
        errors.Add(nameof(Todo.IsCompleted), ["Cannot add completed todo"]);
    }
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }
    return await next(context);
});

app.Run();

public record Todo(int Id, string Name, DateTime DueDate, bool IsCompleted);

interface ITaskService
{
    Todo? GetTodoById(int id);
    List<Todo> GetTodos();
    void DeleteTodos();
    void DeleteTodoById(int id);
    Todo AddTodo(Todo task);
    Todo? UpdateTodo(int id, Todo task);
}

class InMemoryTaskService : ITaskService
{
    private readonly List<Todo> _todos = [];
    private int _nextId = 1;

    public Todo AddTodo(Todo task)
    {
        var newTodo = task with { Id = _nextId++ };
        _todos.Add(newTodo);
        return newTodo;
    }

    public void DeleteTodoById(int id)
    {
        _todos.RemoveAll(task => id == task.Id);
    }

    public Todo? GetTodoById(int id)
    {
        return _todos.SingleOrDefault(t => id == t.Id);
    }

    public List<Todo> GetTodos()
    {
        return _todos;
    }    

    public void DeleteTodos()
    {
        _todos.Clear();
    }
    public Todo? UpdateTodo(int id, Todo task)
    {
        var index = _todos.FindIndex(t => t.Id == id);
        if (index == -1) return null;
        
        var updatedTodo = task with { Id = id };
        _todos[index] = updatedTodo;
        return updatedTodo;
    }
}