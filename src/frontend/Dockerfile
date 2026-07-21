# Multi-stage: bundle the OTel-Web setup with esbuild, then serve statically with nginx.
# No runtime CDN dependency — the bundle is self-contained.
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json ./
RUN npm install
COPY otel-init.js index.html ./
RUN npm run build && cp index.html dist/index.html

FROM nginx:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
