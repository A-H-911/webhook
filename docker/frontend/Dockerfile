FROM node:22-alpine AS build
WORKDIR /app

COPY frontend/webhook-spa/package*.json ./
RUN npm ci --ignore-scripts

COPY frontend/webhook-spa/ ./
RUN npm run build -- --configuration production

FROM nginx:alpine AS runtime

COPY docker/frontend/nginx-maps.conf /etc/nginx/conf.d/00-maps.conf
COPY docker/frontend/nginx.conf      /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/webhook-spa/browser /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
