output "graphql_url" {
  value = aws_appsync_graphql_api.main.uris["GRAPHQL"]
}

output "realtime_url" {
  value = aws_appsync_graphql_api.main.uris["REALTIME"]
}

output "api_id" {
  value = aws_appsync_graphql_api.main.id
}
