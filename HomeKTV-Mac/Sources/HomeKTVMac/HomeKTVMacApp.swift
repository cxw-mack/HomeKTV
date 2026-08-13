import SwiftUI

@main
struct HomeKTVMacApp: App {
    @StateObject private var store = KTVStore()

    var body: some Scene {
        WindowGroup {
            MainView()
                .environmentObject(store)
                .onAppear { store.restoreLibrary() }
        }
        .defaultSize(width: 1320, height: 800)
    }
}
