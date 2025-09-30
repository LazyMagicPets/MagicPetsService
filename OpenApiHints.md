# OpenAPI Specification Best Practices for NSwag/LazyMagic Code Generation

This document contains heuristics and best practices learned from iterative debugging of OpenAPI specifications with NSwag/LazyMagic code generation tools.

## 1. Schema Organization

### Always Extract Inline Types
**Problem:** Inline enums and anonymous objects generate generic type names like `SortBy`, `Response`, `Body`, `Period2`, `TimeRange2`

**Solution:** Extract ALL inline definitions to named schema components
```yaml
# BAD - Inline enum
parameters:
  - name: sortBy
    schema:
      type: string
      enum: [name, date, rating]

# GOOD - Referenced enum
parameters:
  - name: sortBy
    schema:
      $ref: '#/components/schemas/BusinessSortBy'

components:
  schemas:
    BusinessSortBy:
      type: string
      enum: [name, date, rating]
```

### Extract Nested Objects
**Problem:** Nested anonymous objects generate numbered types like `Period2`, `Trends`, `Dependencies`

**Solution:** Extract nested objects as separate schemas
```yaml
# BAD - Nested anonymous object
BusinessAnalyticsResponse:
  properties:
    period:
      type: object
      properties:
        startDate:
          type: string
        endDate:
          type: string

# GOOD - Referenced nested object
BusinessAnalyticsResponse:
  properties:
    period:
      $ref: '#/components/schemas/BusinessAnalyticsPeriod'

BusinessAnalyticsPeriod:
  type: object
  properties:
    startDate:
      type: string
    endDate:
      type: string
```

## 2. Request/Response Bodies

### Always Use Named Schemas for Request Bodies
**Problem:** Inline request bodies generate generic names like `Body`, `Body2`

**Solution:** Create named request schemas
```yaml
# BAD - Inline request body
requestBody:
  content:
    application/json:
      schema:
        type: object
        properties:
          status:
            type: string

# GOOD - Named request schema
requestBody:
  content:
    application/json:
      schema:
        $ref: '#/components/schemas/BusinessStatusUpdateRequest'
```

## 3. Date/Time Formats

### Use date-time Format, Not date
**Problem:** `format: date` causes `DateFormatConverter` not found errors

**Solution:** Always use `format: date-time` for date fields
```yaml
# BAD
issuedDate:
  type: string
  format: date

# GOOD
issuedDate:
  type: string
  format: date-time
```

## 4. Naming Conflicts

### Avoid Reserved or Conflicting Names
**Problem:** Names like `BusinessModel` may conflict with LazyMagic-generated model classes

**Solution:** Use more specific names
```yaml
# BAD
BusinessModel:
  type: string
  enum: [b2b, b2c]

# GOOD
BusinessModelType:
  type: string
  enum: [b2b, b2c]
```

### Avoid Duplicate Schema Names Across Files
**Problem:** Common schemas like `Error` defined in multiple schema files cause ambiguous reference errors (`CS0104`)

**Solution:** Use unique prefixed names for each schema file
```yaml
# BAD - Same name in multiple files
# In business-schema.yaml:
Error:
  type: object
  
# In match-schema.yaml:
Error:
  type: object

# GOOD - Unique names per schema
# In business-schema.yaml:
BusinessError:
  type: object
  
# In match-schema.yaml:
MatchError:
  type: object
```

## 5. Parameter Nullability

### Be Consistent with Optional Parameters
**Problem:** NSwag generates inconsistent nullability between interface and controller for optional parameters with defaults

**Solution:** For optional parameters, remove default values from schema to ensure consistent nullable types
```yaml
# PROBLEMATIC - May cause interface/controller mismatch
parameters:
  - name: page
    in: query
    required: false
    schema:
      type: integer
      default: 1  # This can cause nullability inconsistency

# GOOD - Consistent nullable generation
parameters:
  - name: page
    in: query
    required: false
    schema:
      type: integer
      minimum: 1
      # No default value - handle in implementation
```

## 6. Enum Definitions

### Define Enums as Separate Components
**Problem:** Inline enums in parameters generate generic type names

**Solution:** Always extract enums to component schemas
```yaml
# Extract ALL enums including:
- Sort orders
- Time ranges  
- Status values
- Any repeated string literals
```

## 7. Schema Consolidation

### Avoid Duplicate Input/Response Types When Possible
**Problem:** Having separate `ProductInput` and `ProductResponse` types increases complexity

**Solution:** Use a single `Product` schema with optional fields where appropriate
```yaml
# Consider consolidating when:
- Fields are mostly the same
- Only difference is generated fields (id, timestamps)
- Use optional properties for request vs response differences
```

## 8. Anonymous Response Schemas

### Extract All Response Structures
**Problem:** Anonymous response objects in endpoints generate generic names

**Solution:** Create named response schemas for all endpoints
```yaml
# Create specific response schemas:
- BusinessStatsResponse
- BusinessVerificationResponse
- BusinessAnalyticsResponse
- BusinessHealthResponse
```

## 9. Query Parameter Arrays

### Use Proper Array Serialization
**Problem:** Array parameters need explicit style/explode settings

**Solution:** Specify serialization format for array parameters
```yaml
parameters:
  - name: industry
    in: query
    schema:
      type: array
      items:
        type: string
    style: form
    explode: false
```

## 10. Pre-Generation Checklist

Before generating code from OpenAPI specs:

- [ ] All inline enums extracted to named schemas
- [ ] All inline request/response bodies extracted to named schemas
- [ ] All nested anonymous objects extracted to named schemas
- [ ] All date fields use `format: date-time` not `format: date`
- [ ] No naming conflicts with framework-generated classes
- [ ] Optional parameters don't have default values in schema (handle in code)
- [ ] All array parameters have style/explode specified
- [ ] Request/response types consolidated where appropriate
- [ ] All schemas that will be generated are actually used in the API

## 11. Debugging Generated Code

When encountering compilation errors:

1. **Missing Type Errors** (`CS0246`): Usually indicates inline types that need extraction
2. **Interface Implementation Errors** (`CS0535`): Check parameter nullability consistency
3. **Duplicate Type Errors** (`CS0101`): Check for naming conflicts with generated code
4. **Ambiguous Type References** (`CS0104`): Use type aliases or rename conflicting types

## 12. Schema References in LazyMagic

### Always Use Local References in API Files
**Problem:** External schema file references like `'openapi.consumer-schema.yaml#/components/schemas/Consumer'` cause schema not found errors

**Solution:** Use local references assuming LazyMagic will merge schemas during generation
```yaml
# BAD - External file reference
requestBody:
  content:
    application/json:
      schema:
        $ref: 'openapi.consumer-schema.yaml#/components/schemas/ConsumerRegistrationRequest'

# GOOD - Local reference
requestBody:
  content:
    application/json:
      schema:
        $ref: '#/components/schemas/ConsumerRegistrationRequest'
```

**Explanation:** LazyMagic merges separate schema files during code generation. The system automatically handles the following:
- Merging schema files (like `openapi.consumer-schema.yaml`) with their corresponding API files (like `openapi.consumer.yaml`)
- Including shared schemas from `openapi.shared-schema.yaml` across all modules
- The generated `openapi.g.yaml` contains all schemas in a single document

**Note:** The SharedSchema (containing schemas from `openapi.shared-schema.yaml`) is automatically available to all modules. Common schemas like `Pagination`, `SortOrder`, `TimeRange`, `Error`, etc. defined in the shared schema file can be referenced directly with local references from any module without explicit imports.

## 13. Clean Up Unused Schemas

After refactoring, identify and remove unused schemas:
- Check which schemas are directly referenced in API paths
- Remove schemas only used internally that aren't part of the API surface
- Keep schemas that are part of the public API contract

## Summary

The key principle is: **Extract everything to named components**. Never use inline definitions for:
- Enums
- Request bodies
- Response bodies  
- Nested objects
- Repeated structures

This ensures NSwag/LazyMagic generates clean, properly-named types that compile without conflicts.