PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS resources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    original_name TEXT NOT NULL,
    path TEXT NOT NULL UNIQUE,
    extension TEXT,
    size_bytes INTEGER NOT NULL DEFAULT 0,
    modified_at TEXT,
    imported_at TEXT NOT NULL,
    duration_ms INTEGER,
    note TEXT,
    is_favorite INTEGER NOT NULL DEFAULT 0,
    is_missing INTEGER NOT NULL DEFAULT 0,
    last_opened_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS collections (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS resource_collections (
    resource_id INTEGER NOT NULL,
    collection_id INTEGER NOT NULL,
    PRIMARY KEY (resource_id, collection_id),
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY (collection_id) REFERENCES collections(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS tags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    color TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS resource_tags (
    resource_id INTEGER NOT NULL,
    tag_id INTEGER NOT NULL,
    PRIMARY KEY (resource_id, tag_id),
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS scan_folders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    last_scan_at TEXT,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_resources_title ON resources(title);
CREATE INDEX IF NOT EXISTS idx_resources_path ON resources(path);
CREATE INDEX IF NOT EXISTS idx_resources_modified_at ON resources(modified_at);
CREATE INDEX IF NOT EXISTS idx_resources_imported_at ON resources(imported_at);
CREATE INDEX IF NOT EXISTS idx_resources_size_bytes ON resources(size_bytes);
CREATE INDEX IF NOT EXISTS idx_resources_duration_ms ON resources(duration_ms);
CREATE INDEX IF NOT EXISTS idx_resources_is_favorite ON resources(is_favorite);
CREATE INDEX IF NOT EXISTS idx_resources_is_missing ON resources(is_missing);
