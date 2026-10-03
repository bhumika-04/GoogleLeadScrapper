import type { NextConfig } from "next";

// Where the web server forwards /api/* (same machine by default). Read at build time and baked into the
// standalone server, so set it before `next build` if the API runs elsewhere.
const apiUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5264";

const nextConfig: NextConfig = {
  reactCompiler: true,
  // Self-contained server (.next/standalone/server.js) for running as a Windows service without node_modules.
  output: "standalone",
  // The browser only talks to this site; /api is proxied to DeepLead.Api (no CORS, one address to expose).
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }];
  },
};

export default nextConfig;
