// Standalone Aether desktop shell for the bundled local AI chat interface.
// The device-service CLI is deliberately not spawned or packaged here.

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("error while running Aether Desktop");
}
