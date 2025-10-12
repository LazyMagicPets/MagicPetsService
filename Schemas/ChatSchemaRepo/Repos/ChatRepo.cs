using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

// ChatRepo is now a pure data access layer with no business logic
// All orchestration is handled by ChatManagerService
public partial interface IChatRepo : IDocumentRepo<Chat>
{
    // No additional methods - all operations go through ChatManagerService
}

// Pure data layer implementation - no ChatManagerService dependency
// Constructor is provided by generated code in ChatRepo.g.cs
public partial class ChatRepo : DYDBRepository<Chat>, IChatRepo
{
    // All CRUD operations use base class implementation
    // No overrides needed - ChatManagerService handles coordination
}
