# Repository Enhancement Guidelines for LazyMagic

This document provides guidelines for extending LazyMagic-generated repositories with custom functionality.

## Core Principles

1. **Repositories are for data operations** - Not just CRUD, but also List, Query, and Search operations
2. **Use partial classes** - Extend generated repos without modifying `.g.cs` files
3. **Leverage dependency injection** - Use custom factory registrations when needed
4. **Follow LazyMagic patterns** - Work with the framework, not against it

## Repository Operation Types

LazyMagic repositories support more than just CRUD:

### Standard Operations (Auto-generated)
- **Create** - `CreateAsync(callerInfo, entity)`
- **Read** - `ReadAsync(callerInfo, id)`
- **Update** - `UpdateAsync(callerInfo, entity)`
- **Delete** - `DeleteAsync(callerInfo, id)`
- **List** - `ListAsync(callerInfo)`

### Custom List/Query Operations
These belong in repository partial classes:
- **ListBy{Property}** - Query by specific properties
- **Find{Criteria}** - Complex search operations
- **Match{Pattern}** - Pattern matching operations
- **Search{Term}** - Full-text or semantic search

## Extending Repositories

### 1. Create a Partial Class

```csharp
// Schemas/{Schema}Repo/Repos/{Entity}Repo.cs
namespace {Schema}Repo;

public partial interface I{Entity}Repo : IDocumentRepo<{Entity}> 
{
    // Add custom method signatures
    Task<ActionResult<CustomResponse>> CustomMethodAsync(ICallerInfo callerInfo, CustomRequest request);
}

public partial class {Entity}Repo : DYDBRepository<{Entity}>, I{Entity}Repo
{
    // Add properties for injected services
    public ICustomService CustomService { get; set; }
    
    // Implement custom methods
    public async Task<ActionResult<CustomResponse>> CustomMethodAsync(ICallerInfo callerInfo, CustomRequest request)
    {
        return await CustomService.ProcessAsync(callerInfo, request);
    }
}
```

### 2. Inject Dependencies

When your repository needs additional services:

```csharp
// Schemas/{Schema}Repo/ServiceRepoExtensions.cs
namespace {Schema}Repo;

public static partial class {Schema}RepoExtensions
{
    static partial void AddCustom(IServiceCollection services)
    {
        // Register required services
        services.AddScoped<ICustomService, CustomService>();
        
        // Override default repo registration with factory
        services.AddTransient<I{Entity}Repo>(serviceProvider =>
        {
            var dynamoDb = serviceProvider.GetRequiredService<IAmazonDynamoDB>();
            var customService = serviceProvider.GetRequiredService<ICustomService>();
            
            var repo = new {Entity}Repo(dynamoDb)
            {
                CustomService = customService
            };
            
            return repo;
        });
    }
}
```

### 3. Add to OpenAPI

For API endpoints, add x-lz-gencall directives:

```yaml
paths:
  /custom-endpoint:
    post:
      operationId: customOperation
      x-lz-gencall: {Entity}Repo.CustomMethodAsync(callerInfo, body)
```

## Common Patterns

### Pattern 1: Semantic/Vector Search
```csharp
public partial interface IProductRepo
{
    Task<ActionResult<MatchResponse>> FindMatchesAsync(ICallerInfo callerInfo, MatchRequest request);
}

public partial class ProductRepo
{
    public IVectorSearchService VectorSearchService { get; set; }
    
    public async Task<ActionResult<MatchResponse>> FindMatchesAsync(ICallerInfo callerInfo, MatchRequest request)
    {
        // Delegate to specialized service
        return await VectorSearchService.SearchAsync(callerInfo, request);
    }
}
```

### Pattern 2: Index-based Queries
```csharp
public partial class OrderRepo
{
    public async Task<ObjectResult> ListByCustomerIdAsync(ICallerInfo callerInfo, string customerId, int limit = 0)
    {
        // Use DynamoDB GSI
        return await ListAsync(callerInfo, "GSI1", customerId, limit);
    }
}
```

### Pattern 3: Complex Aggregations
```csharp
public partial class SalesRepo
{
    public async Task<ActionResult<SalesReport>> GetMonthlyReportAsync(ICallerInfo callerInfo, DateTime month)
    {
        // Complex query logic
        var sales = await ListByMonthAsync(callerInfo, month);
        var report = CalculateMetrics(sales);
        return new OkObjectResult(report);
    }
}
```

## Anti-Patterns to Avoid

### ❌ DON'T: Create wrapper repositories
```csharp
// Bad - unnecessary wrapper
public class MatchingRepo : IMatchingRepo 
{
    private readonly IMatchRepo _matchRepo;
    public Task<ActionResult<MatchResponse>> FindMatchesAsync(...) 
    {
        return _matchRepo.FindMatchesAsync(...);
    }
}
```

### ❌ DON'T: Put business logic in controllers
```csharp
// Bad - logic should be in repo or service
public class ModuleController 
{
    public override async Task<ActionResult<Result>> MethodAsync(Request request)
    {
        // Complex business logic here - WRONG!
    }
}
```

### ❌ DON'T: Modify generated files
```csharp
// Never edit .g.cs files - they will be overwritten
```

### ✅ DO: Use repositories for data operations
```csharp
// Good - repo handles data operations
public partial class ProductRepo
{
    public async Task<ActionResult<SearchResults>> SearchAsync(ICallerInfo callerInfo, SearchCriteria criteria)
    {
        // Data operation logic here
    }
}
```

## Service vs Repository

### Use a Repository when:
- Performing data operations (CRUD, List, Search)
- Working with a specific entity type
- Need LazyMagic auto-injection
- Operations are data-centric

### Use a Service when:
- Orchestrating multiple repositories
- Implementing business logic
- Integrating external APIs
- Operations span multiple entities

## Dependency Injection Tips

1. **Services are registered in AddCustom()** - Use the partial method
2. **Override repo registration** - Use factory pattern for complex initialization
3. **Property injection** - Use public properties with `{ get; set; }`
4. **Constructor injection** - Available but requires factory registration

## Testing Custom Repositories

```csharp
[Fact]
public async Task CustomMethod_Should_ReturnExpectedResults()
{
    // Arrange
    var mockService = new Mock<ICustomService>();
    var repo = new CustomRepo(mockDynamoDb)
    {
        CustomService = mockService.Object
    };
    
    // Act
    var result = await repo.CustomMethodAsync(callerInfo, request);
    
    // Assert
    Assert.NotNull(result.Value);
}
```

## Migration Guide

When refactoring existing code to follow these patterns:

1. **Identify the operation type** - Is it data-related?
2. **Find the appropriate repo** - Which entity does it operate on?
3. **Add to partial class** - Extend the interface and implementation
4. **Update DI if needed** - Add custom registration
5. **Update OpenAPI** - Add x-lz-gencall directive
6. **Remove old code** - Clean up wrappers and overrides
7. **Test thoroughly** - Ensure DI and functionality work

## Example: Complete Enhancement

See the MatchRepo enhancement as a complete example:

1. Added matching methods to IMatchRepo interface (partial)
2. Implemented methods in MatchRepo class (partial)
3. Injected IMatchingService via custom DI registration
4. Updated OpenAPI with x-lz-gencall directives
5. Removed unnecessary wrapper classes
6. Cleaned up controller overrides

This pattern keeps code clean, follows LazyMagic conventions, and maintains separation of concerns.