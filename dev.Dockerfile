# Development image: docker build -f dev.Dockerfile -t messfsharp-dev . && docker run --rm -it -v "$PWD":/workspace messfsharp-dev
FROM mcr.microsoft.com/dotnet/sdk:10.0.302
WORKDIR /workspace
COPY . .
CMD ["dotnet", "test"]
