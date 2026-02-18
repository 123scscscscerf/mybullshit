<?php

declare(strict_types=1);

require_once __DIR__ . '/../includes/db.php';

$pdo = db();
$fileId = isset($_GET['id']) ? sanitizeFileId((string) $_GET['id']) : '';

if ($fileId === '') {
    http_response_code(400);
    echo 'Missing file ID.';
    exit;
}

$file = fetchFileById($pdo, $fileId);
if ($file === null) {
    http_response_code(404);
    echo 'File not found.';
    exit;
}

$storagePath = __DIR__ . '/../files/' . basename($file['stored_name']);
if (!is_file($storagePath)) {
    http_response_code(404);
    echo 'Stored file missing.';
    exit;
}

header('Content-Description: File Transfer');
header('Content-Type: application/octet-stream');
header('Content-Disposition: attachment; filename="' . rawurlencode($file['original_name']) . '"');
header('Content-Length: ' . filesize($storagePath));
header('X-Content-Type-Options: nosniff');

readfile($storagePath);
exit;
