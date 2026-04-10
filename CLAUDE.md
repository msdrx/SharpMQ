# SharpMQ - Project Instructions

## Important: Do Not Run the Project

This project uses Docker containers for debugging. **Never** run the project (`dotnet run`, `docker-compose up`, etc.) as it consumes too much memory and crashes the Docker sandbox VM.

- Use `dotnet build` to verify compilation
- Use `dotnet test` to run tests
- Do **not** use `dotnet run`, `docker-compose up`, or any command that starts the application or its containers
