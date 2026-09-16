export interface ProjectDriveLink {
  googleDriveFolderUrl: string | null;
}

export function isValidGoogleDriveLink(value: string): boolean {
  const link = value.trim();
  if (!link) return true;
  if (link.length > 2048 || /[\s\u0000-\u001f\u007f\\]/.test(link)) return false;
  try {
    const url = new URL(link);
    return url.protocol === 'https:' && url.hostname === 'drive.google.com'
      && !url.port && !url.username && !url.password && url.pathname !== '/';
  } catch { return false; }
}
