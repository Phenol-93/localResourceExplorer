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

CREATE TABLE IF NOT EXISTS collection_categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    collection_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (collection_id, name),
    FOREIGN KEY (collection_id) REFERENCES collections(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS collection_tags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    collection_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    color TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (collection_id, name),
    FOREIGN KEY (collection_id) REFERENCES collections(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS resource_placements (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    resource_id INTEGER NOT NULL,
    collection_id INTEGER NOT NULL,
    category_id INTEGER,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY (collection_id) REFERENCES collections(id) ON DELETE CASCADE,
    FOREIGN KEY (category_id) REFERENCES collection_categories(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS resource_placement_tags (
    placement_id INTEGER NOT NULL,
    tag_id INTEGER NOT NULL,
    PRIMARY KEY (placement_id, tag_id),
    FOREIGN KEY (placement_id) REFERENCES resource_placements(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES collection_tags(id) ON DELETE CASCADE
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

CREATE INDEX IF NOT EXISTS idx_collection_categories_collection_id ON collection_categories(collection_id);
CREATE INDEX IF NOT EXISTS idx_collection_categories_sort ON collection_categories(collection_id, sort_order, name);
CREATE INDEX IF NOT EXISTS idx_collection_tags_collection_id ON collection_tags(collection_id);
CREATE INDEX IF NOT EXISTS idx_collection_tags_name ON collection_tags(name);
CREATE INDEX IF NOT EXISTS idx_collection_tags_sort ON collection_tags(collection_id, sort_order, name);
CREATE INDEX IF NOT EXISTS idx_resource_placements_resource_id ON resource_placements(resource_id);
CREATE INDEX IF NOT EXISTS idx_resource_placements_collection_id ON resource_placements(collection_id);
CREATE INDEX IF NOT EXISTS idx_resource_placements_category_id ON resource_placements(category_id);
CREATE INDEX IF NOT EXISTS idx_resource_placements_collection_category_resource
    ON resource_placements(collection_id, category_id, resource_id);
CREATE INDEX IF NOT EXISTS idx_resource_placements_resource_collection_category
    ON resource_placements(resource_id, collection_id, category_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_resource_placements_unique_no_category
    ON resource_placements(resource_id, collection_id)
    WHERE category_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS idx_resource_placements_unique_category
    ON resource_placements(resource_id, collection_id, category_id)
    WHERE category_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_resource_placement_tags_placement_id ON resource_placement_tags(placement_id);
CREATE INDEX IF NOT EXISTS idx_resource_placement_tags_tag_id ON resource_placement_tags(tag_id);
CREATE INDEX IF NOT EXISTS idx_resource_placement_tags_tag_placement
    ON resource_placement_tags(tag_id, placement_id);
