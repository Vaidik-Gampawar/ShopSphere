# HTTP API reference

Default base URLs are `http://localhost:5101` Product, `:5102` Order, `:5103` Identity, `:5104` Notification. JSON uses camelCase. Authentication: `Authorization: Bearer <token>`. Administrator routes require the `Administrator` role. Internal routes use `X-Service-Key` and should only be called by trusted services. All processes expose anonymous `GET /health`.

## IdentityService

| Method and path | Access | Body / result |
| --- | --- | --- |
| POST `/api/auth/register` | Public, rate limited | `{name,email,password}` → 201 AuthDto; Customer role only |
| POST `/api/auth/login` | Public, rate limited | `{email,password}` → 200 AuthDto |
| GET `/api/auth/me` | Authenticated | Current validated identity |
| GET `/api/auth/admin-check` | Admin | 200 authorization check |

AuthDto: `{token,expiresUtc,userId,name,email,role}`. Registration passwords require 12–128 characters, upper/lowercase, digit and symbol. Duplicate emails return 409; incorrect login returns 401; rate limit returns 429.

## ProductService

| Method and path | Access | Behavior |
| --- | --- | --- |
| GET `/api/products?search=&categoryId=1&page=1&pageSize=12` | Public | Page `{items,pageNumber,pageSize,totalCount,totalPages}`, pageSize 1–100 |
| GET `/api/products?includeInactive=true` | Admin for inactive results | Same page contract |
| GET `/api/products/{id}` | Public / Admin | Active product / admin may see inactive |
| POST `/api/products` | Admin | ProductInput → 201 ProductDto |
| PUT `/api/products/{id}` | Admin | ProductInput → 200; stock is preserved |
| DELETE `/api/products/{id}` | Admin | Deactivate, 204 |
| POST `/api/products/{id}/inventory` | Admin | `{delta:5}` or negative delta, 204; conflict 409 |
| GET `/api/categories` | Public | CategoryDto array |
| POST `/api/categories` | Admin | `{name}` → 201 CategoryDto |
| PUT `/api/categories/{id}` | Admin | `{name}` → 204 |
| POST `/internal/reservations` | Service key | `{orderId,items:[{productId,quantity}]}` → snapshots |
| DELETE `/internal/reservations/{orderId}` | Service key | Idempotent release, 204 |

ProductInput: `{name,description,price,stock,categoryId,isActive}`. Creation uses stock; edit uses the inventory endpoint for stock changes. ProductDto adds `id` and `category`. Price range 0.01–1,000,000; stock range 0–1,000,000. Reservation items must be distinct, 1–50 lines, quantities 1–99. Invalid product/category is 400 or 404; insufficient inventory or conflicting request ID is 409.

## OrderService

| Method and path | Access | Behavior |
| --- | --- | --- |
| POST `/api/orders` | Authenticated | PlaceOrderInput → 201 final state / 202 pending inventory |
| GET `/api/orders?page=1&pageSize=20` | Authenticated | Own history page |
| GET `/api/orders?all=true` | Admin for all results | All order history page |
| GET `/api/orders/{id}` | Owner or Admin | OrderDto; hidden from others as 404 |
| PUT `/api/orders/{id}/status` | Admin | `{status:"Processing"}` → 204; invalid transition 409 |

PlaceOrderInput:

```json
{
  "requestId": "f07d1454-ed44-4e28-89f7-ea6994b91887",
  "shippingAddress": "123 Example Street, Sample City",
  "items": [{"productId": 1, "quantity": 2}]
}
```

Use a new nonempty UUID for a new purchase; reuse it with the same body if the result is uncertain. Different bodies/users with the same UUID return 409. Never generate a fresh ID merely to retry an uncertain request.

OrderDto: `{id,userId,createdUtc,status,shippingAddress,total,items:[{productId,productName,unitPrice,quantity}],notificationSent}`. Inspect `status`: `PendingInventory`, `Confirmed`, `Rejected`, `Processing`, `Shipped`, `Delivered`, `CancelPending`, `Cancelled`. The 201 response can contain `Rejected` when inventory refuses the purchase; the persisted intent remains available for history. For pending states, poll GET. Admin transitions follow the state machine in ARCHITECTURE.md.

## NotificationService

| Method and path | Access | Behavior |
| --- | --- | --- |
| GET `/api/notifications?page=1&pageSize=20` | Authenticated | Own notification array, newest first, pageSize ≤100 |
| POST `/internal/notifications` | Service key | `{orderId,userId,email,total}` → idempotent confirmation |

NotificationDto: `{id,orderId,userId,email,message,createdUtc}`. History is a local delivery sink; no actual email is sent.

## Errors

Validation failures return 400 validation problem JSON (`errors` per field). Domain errors use problem JSON with status/title/traceId. Authentication failure is 401, authorization failure 403, hidden/missing resources 404, conflicts 409, dependency outages 503, unexpected faults 500. Internal exception details appear only in logs. In order processing, dependency failures are retained as pending state and retried instead of discarding a purchase intent.
