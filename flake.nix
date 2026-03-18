{
  description = "Shmoxy - HTTP/HTTPS Intercepting Proxy";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-compat.url = "github:edolstra/flake-compat";
  };

  outputs =
    { self, nixpkgs, ... }:
    let
      supportedSystems = [
        "aarch64-darwin"
        "x86_64-darwin"
        "aarch64-linux"
        "x86_64-linux"
      ];
      forEachSystem = f: nixpkgs.lib.genAttrs supportedSystems (system: f system);
      pkgsFor =
        system:
        import nixpkgs {
          inherit system;
          config.allowUnfree = true;
        };
    in
    {
      devShells = forEachSystem (
        system:
        let
          pkgs = pkgsFor system;
        in
        {
          default = pkgs.mkShell {
            buildInputs = [ pkgs.dotnetCorePackages.sdk_10_0 ];
          };
        }
      );

      packages = forEachSystem (
        system:
        let
          pkgs = pkgsFor system;
        in
        {
          shmoxy = pkgs.buildDotnetModule {
            pname = "shmoxy";
            version = "0.1.0";
            src = ./.;
            projectFile = "src/shmoxy/shmoxy.csproj";
            nugetDeps = ./deps.nix;

            dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
            dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
          };

          default = self.packages.${system}.shmoxy;
        }
      );
    };
}
