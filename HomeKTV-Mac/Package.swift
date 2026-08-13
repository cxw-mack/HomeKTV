// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "HomeKTVMac",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "HomeKTVMac", targets: ["HomeKTVMac"])],
    targets: [
        .executableTarget(name: "HomeKTVMac"),
        .testTarget(name: "HomeKTVMacTests", dependencies: ["HomeKTVMac"])
    ]
)
