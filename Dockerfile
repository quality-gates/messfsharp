# Runtime image: docker build -t messfsharp . && docker run --rm -v "$PWD":/code messfsharp /code text fsharp
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/MessFSharp/MessFSharp.fsproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
WORKDIR /code
ENTRYPOINT ["dotnet", "/app/messfsharp.dll"]
CMD ["--help"]

