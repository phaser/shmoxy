{
  description = "Shmoxy - HTTP/HTTPS Proxy Server with TLS Termination";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-compat.url = "github:edolstra/flake-compat";
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-compat,
    }:
    let
      supportedSystems = [
        "aarch64-darwin"
        "x86_64-darwin"
        "aarch64-linux"
        "x86_64-linux"
      ];

      forEachSystem = f: nixpkgs.lib.genAttrs supportedSystems (system: f system);

      pkgsFor = system: import nixpkgs { inherit system; };
    in
    {
      devShells = forEachSystem (
        system:
        let
          pkgs = pkgsFor system;
          # Use .NET SDK 10.0 - matches project target framework
          dotnet-sdk =
            if system == "x86_64-linux" || system == "aarch64-linux" then
              pkgs.dotnetCorePackages.sdk_10_0
            else
              pkgs.dotnet-sdk_10;
        in
        {
          default = pkgs.mkShell rec {
            name = "shmoxy-dev";

            buildInputs = [ dotnet-sdk ];

            shellHook = ''
              echo ""
              echo "=========================================="
              echo "  Shmoxy Development Environment"
              echo "=========================================="
              echo ""
              echo ".NET SDK: $(dotnet --version)"
              echo ""
              echo "Available commands:"
              echo "  dotnet build   - Build the project"
              echo "  dotnet test    - Run tests (skip integration by default)"
              echo "  dotnet run     - Run proxy server on port 8080"
              echo "  dotnet run -- -p <port> -l <level>"
              echo ""
              echo "To enter this shell: nix develop"
              echo "=========================================="
              echo "";
            '';
          };
        }
      );

      packages = forEachSystem (
        system:
        let
          pkgs = pkgsFor system;
          dotnet-sdk =
            if system == "x86_64-linux" || system == "aarch64-linux" then
              pkgs.dotnetCorePackages.sdk_10_0
            else
              pkgs.dotnet-sdk_10;
        in
        {
          shmoxy = pkgs.buildDotnetModule (finalAttrs: {
            pname = "shmoxy";
            version = "0.1.0";

            src = ./.;

            buildInputs = [ dotnet-sdk ];

            projectFile = "src/shmoxy/shmoxy.csproj";
          });
        }
      );

      defaultPackage = self.packages.aarch64-darwin.shmoxy;
    };
}
