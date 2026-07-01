cask "beaver-board" do
  version "0.1.0"
  sha256 :no_check # Preview builds are unsigned; checksum verified manually for releases

  url "https://github.com/Danissimode/BeaverBoardKanban/releases/download/v#{version}/BeaverBoard-#{version}-macOS-arm64.dmg"
  name "Beaver Board"
  desc "Local-first Kanban orchestrator for AI coding agents"
  homepage "https://github.com/Danissimode/BeaverBoardKanban"

  app "BeaverBoard.app"

  zap trash: [
    "~/Library/Application Support/BeaverBoard",
    "~/Library/Caches/BeaverBoard",
    "~/Library/Logs/BeaverBoard",
    "~/Library/Preferences/io.beaverboard.app.plist",
  ]
end
