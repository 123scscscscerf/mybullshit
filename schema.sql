CREATE TABLE files (
    id VARCHAR(64) PRIMARY KEY,
    original_name TEXT NOT NULL,
    stored_name TEXT NOT NULL,
    size INTEGER NOT NULL,
    uploaded_at DATETIME NOT NULL
);
