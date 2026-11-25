# Cook With What You Have

Aplicación web para cocinar con lo que hay en la nevera. Escribes los ingredientes, eliges si quieres aprovechar sobras y el aparato (sartén u horno, airfryer u olla rápida) y recibes una receta en español. La receta se apoya en un recetario tradicional guardado en PostgreSQL. Si pegas un código de barras, Open Food Facts aporta alérgenos y Nutri-Score. Si faltan ingredientes, Superfudis intenta comparar precios. Si una de esas dos fuentes no responde, la receta se muestra igual.

La redacción y los embeddings los hace el proveedor indicado en `.env`: Gemini o Cloudflare. Los dos espacios de embeddings no son compatibles. Si cambias de uno a otro hay que vaciar la tabla `recipes` para que se vuelva a indexar.

## Requisitos

- Docker Desktop
- .NET SDK 10, solo si vas a ejecutar las pruebas fuera de Docker

## Arranque

En la raíz del repositorio:

```bash
cp .env.example .env
```

Abre `.env` y deja un proveedor:

- `AI_PROVIDER=Gemini` y pega la clave de [Google AI Studio](https://aistudio.google.com/apikey) en `GEMINI_API_KEY`.
- `AI_PROVIDER=Cloudflare` si quieres la alternativa gratuita, sin tarjeta. En [dash.cloudflare.com](https://dash.cloudflare.com) crea una cuenta, copia el Account ID de la barra lateral y crea un token en My Profile → API Tokens con permiso Workers AI. Pégalos en `CLOUDFLARE_ACCOUNT_ID` y `CLOUDFLARE_API_TOKEN`. El plan gratis incluye 10.000 neurons al día, suficientes para probar recetas.

Gemini sigue siendo la opción por defecto.

```bash
docker compose up --build -d
```

La primera vez, con la clave puesta y la tabla vacía, la API embebe unas 25 recetas tradicionales antes de abrir el puerto. Puede tardar un minuto. Cuando termine:

- Web: http://localhost:8081
- API: http://localhost:8080/health

Sin clave la API arranca, pero generar una receta responde 503.

## Cambiar de Gemini a Cloudflare

1. Rellena `CLOUDFLARE_API_TOKEN` y `CLOUDFLARE_ACCOUNT_ID`, y pon `AI_PROVIDER=Cloudflare`.
2. Vacía el índice, porque los vectores de Gemini no sirven para Cloudflare:

```bash
docker compose exec db psql -U cook -d cookwithwhatyouhave -c "truncate recipes;"
```

3. Recrea la API para que lea `.env` y vuelva a sembrar:

```bash
docker compose up -d --force-recreate --no-deps api
```

Para volver a Gemini, el mismo proceso con `AI_PROVIDER=Gemini` y `truncate recipes`.

## Probar una receta

En la web, escribe `pan duro, ajo y huevos`, marca sobras y pulsa Generar receta. Para el aviso de producto puedes usar el código `8480000171634`.

Las pruebas automáticas no llaman a ningún modelo si las claves no están en el entorno. Con la clave en el entorno, la prueba del proveedor correspondiente hace un embedding y una generación corta:

```bash
set -a && source .env && set +a
dotnet test
```
