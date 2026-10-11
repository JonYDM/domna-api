# domna-api

Backend de **Domna** en **.NET 8** con **Clean Architecture**. Primera entrega: módulo de **login**
(registro / inicio de sesión / recuperación de PIN) y **CRUD de prenda** (producto con variantes
talla × color, stock por sucursal y código de barras). El contrato calca `domna-web/src/types/api.ts`.

## Arrancar
```bash
dotnet run --project src/Domna.Api     # https://localhost:5xxx/swagger
dotnet test                            # 36 pruebas (dominio + servicios)
```
La base es **EF Core InMemory** (demo): al pasar a PostgreSQL solo cambia el provider en
`src/Domna.Infrastructure/DependencyInjection.cs` (`UseInMemoryDatabase` → `UseNpgsql`).

Seed de ejemplo: clienta **María** (`777 123 4567` / PIN `123456`) y la prenda `p-vestido-demo`.

## Arquitectura
```
src/
├── Domna.Domain          entidades + reglas puras (Clienta, Producto, Variante, Normalizacion)
├── Domna.Application      DTOs (contrato), interfaces, AuthService, ProductoService
├── Domna.Infrastructure   EF Core (DbContext + repos), BCrypt, JWT, correo simulado, DI
└── Domna.Api              controladores, JWT auth, Swagger, middleware de errores, seed
tests/
└── Domna.Tests           dominio (variantes, SKU, stock) + servicios (login, recuperación, CRUD)
```
Regla de dependencias: Api → Application/Infrastructure → Domain. El dominio no depende de nada.

## Endpoints

### Auth — `api/auth`
| Método | Ruta | Qué hace |
|---|---|---|
| POST | `/registrar` | Sign up (nombre, apellidos, teléfono +52, correo, PIN 6 díg) → sesión + JWT |
| POST | `/login` | Teléfono + PIN → sesión + JWT |
| POST | `/recuperar` | Olvidé mi PIN: enlace por correo (en demo devuelve el token) |
| POST | `/recuperar/confirmar` | Fija un PIN nuevo con el token (un solo uso) |

### Productos — `api/productos`
| Método | Ruta | Rol | Qué hace |
|---|---|---|---|
| GET | `/` `?incluirInactivos` | público | Lista el catálogo |
| GET | `/{id}` | público | Detalle de la prenda |
| GET | `/codigo/{codigo}` | público | Busca por código de barras (POS) |
| POST | `/` | staff | Alta: genera variantes talla × color con stock y código de barras |
| PUT | `/{id}` | staff | Edita nombre, descripción, precio, costo, oferta, apartable |
| POST | `/{id}/stock` | staff | Ajuste de stock absoluto de una variante en una sucursal |
| DELETE | `/{id}` | dueña | Baja lógica (desactivar) |

*staff = dueña o cajera (vía rol del JWT).*

## Seguridad
- PIN con hash **BCrypt** (work factor 11); nunca viaja ni se guarda en claro.
- **JWT HS256** (claims `sub`, `role`, `telefono`, `nombre`). Clave y vigencia en `appsettings.json`
  (sección `Jwt`); en producción va en variables de entorno / secretos.
- Login con mensaje genérico (no revela qué dato falló). Recuperación: token de un solo uso con
  expiración (30 min) y sin revelar si el correo existe.
